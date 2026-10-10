using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

[Authorize(Roles = "Vendor")]
public class VendorController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public VendorController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Dashboard()
    {
        var store = await OwnStoreAsync();
        if (store is null)
            return View("Missing");

        var orders = await _db.Orders.Where(o => o.StoreId == store.Id).ToListAsync();
        var recent = await _db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Customer)
            .Where(o => o.StoreId == store.Id)
            .OrderByDescending(o => o.CreatedAt)
            .Take(6)
            .ToListAsync();

        return View(new VendorDashboardView
        {
            Store = store,
            ProductCount = await _db.Products.CountAsync(p => p.StoreId == store.Id),
            OpenOrders = orders.Count(o => o.Status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.ReadyForPickup),
            Revenue = orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.Total),
            RecentOrders = recent
        });
    }

    public async Task<IActionResult> Products()
    {
        var store = await OwnStoreAsync();
        if (store is null)
            return View("Missing");
        if (store.ApprovalStatus != StoreApprovalStatus.Approved)
            return RedirectToAction(nameof(Dashboard));

        var products = await _db.Products
            .Include(p => p.Category)
            .Where(p => p.StoreId == store.Id)
            .OrderBy(p => p.Category!.Name)
            .ThenBy(p => p.Name)
            .ToListAsync();
        ViewData["StoreName"] = store.Name;
        return View(products);
    }

    [HttpGet]
    public async Task<IActionResult> EditProduct(int? id)
    {
        var store = await RequireApprovedStoreAsync();
        if (store is null)
            return RedirectToAction(nameof(Dashboard));

        var categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        if (id is null)
        {
            return View(new ProductForm
            {
                Categories = categories,
                CategoryId = categories.FirstOrDefault()?.Id ?? 0,
                Stock = 10,
                IsActive = true
            });
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.StoreId == store.Id);
        if (product is null)
            return NotFound();

        return View(new ProductForm
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            Stock = product.Stock,
            Unit = product.Unit,
            IsActive = product.IsActive,
            Categories = categories
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(ProductForm form)
    {
        var store = await RequireApprovedStoreAsync();
        if (store is null)
            return RedirectToAction(nameof(Dashboard));

        form.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        if (string.IsNullOrWhiteSpace(form.Name))
            form.Error = "Enter a product name.";
        else if (form.Price <= 0)
            form.Error = "Price must be greater than zero.";
        else if (form.Stock < 0)
            form.Error = "Stock cannot be negative.";
        else if (form.Categories.All(c => c.Id != form.CategoryId))
            form.Error = "Choose a category.";
        if (form.Error is not null)
            return View(form);

        Product product;
        if (form.Id == 0)
        {
            product = new Product { StoreId = store.Id, Sku = "HK-" + Random.Shared.Next(10000, 99999) };
            _db.Products.Add(product);
        }
        else
        {
            var existing = await _db.Products.FirstOrDefaultAsync(p => p.Id == form.Id && p.StoreId == store.Id);
            if (existing is null)
                return NotFound();
            product = existing;
        }

        product.CategoryId = form.CategoryId;
        product.Name = form.Name.Trim();
        product.Description = form.Description.Trim();
        product.Price = form.Price;
        product.Stock = form.Stock;
        product.Unit = string.IsNullOrWhiteSpace(form.Unit) ? "pc" : form.Unit.Trim();
        product.IsActive = form.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = form.Id == 0 ? "Product added." : "Product updated.";
        return RedirectToAction(nameof(Products));
    }

    public async Task<IActionResult> Orders(string? status)
    {
        var store = await RequireApprovedStoreAsync();
        if (store is null)
            return RedirectToAction(nameof(Dashboard));

        var query = FullOrders().Where(o => o.StoreId == store.Id);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var parsed))
            query = query.Where(o => o.Status == parsed);

        ViewData["StoreName"] = store.Name;
        ViewData["Status"] = status ?? "";
        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return View(orders);
    }

    public async Task<IActionResult> Order(int id)
    {
        var store = await RequireApprovedStoreAsync();
        if (store is null)
            return RedirectToAction(nameof(Dashboard));
        var order = await FullOrders().FirstOrDefaultAsync(o => o.Id == id && o.StoreId == store.Id);
        if (order is null)
            return NotFound();
        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Accept(int id) => Move(id, OrderStatus.Pending, OrderStatus.Accepted);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Ready(int id) => Move(id, OrderStatus.Accepted, OrderStatus.ReadyForPickup);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Complete(int id) => Move(id, OrderStatus.ReadyForPickup, OrderStatus.Completed);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var store = await RequireApprovedStoreAsync();
        if (store is null)
            return RedirectToAction(nameof(Dashboard));
        var order = await FullOrders().FirstOrDefaultAsync(o => o.Id == id && o.StoreId == store.Id);
        if (order is null)
            return NotFound();
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            TempData["Error"] = "That order can no longer be cancelled.";
            return RedirectToAction(nameof(Order), new { id });
        }

        foreach (var line in order.Lines)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId && p.StoreId == store.Id);
            if (product is not null)
                product.Stock += line.Quantity;
        }

        foreach (var installment in order.Installments.Where(i => i.Status == InstallmentStatus.Scheduled))
            installment.Status = InstallmentStatus.Cancelled;

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order " + order.OrderNumber + " was cancelled and stock was returned.";
        return RedirectToAction(nameof(Order), new { id });
    }

    private async Task<IActionResult> Move(int id, OrderStatus from, OrderStatus to)
    {
        var store = await RequireApprovedStoreAsync();
        if (store is null)
            return RedirectToAction(nameof(Dashboard));
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.StoreId == store.Id);
        if (order is null)
            return NotFound();
        if (order.Status != from)
        {
            TempData["Error"] = "That order is no longer " + StatusText.Label(from).ToLowerInvariant() + ".";
            return RedirectToAction(nameof(Order), new { id });
        }

        order.Status = to;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order " + order.OrderNumber + " is now " + StatusText.Label(to).ToLowerInvariant() + ".";
        return RedirectToAction(nameof(Order), new { id });
    }

    private async Task<Store?> OwnStoreAsync()
    {
        var userId = _users.GetUserId(User);
        return await _db.Stores.FirstOrDefaultAsync(s => s.OwnerId == userId);
    }

    private async Task<Store?> RequireApprovedStoreAsync()
    {
        var store = await OwnStoreAsync();
        if (store is null || store.ApprovalStatus != StoreApprovalStatus.Approved)
            return null;
        return store;
    }

    private IQueryable<Order> FullOrders() =>
        _db.Orders
            .Include(o => o.Lines)
            .Include(o => o.Payment)
            .Include(o => o.Installments)
            .Include(o => o.Customer)
            .Include(o => o.Store);
}
