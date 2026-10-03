using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Dashboard()
    {
        var orders = await _db.Orders.AsNoTracking().ToListAsync();
        var live = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        var lines = await _db.OrderLines
            .AsNoTracking()
            .Include(l => l.Order)
            .Where(l => l.Order!.Status != OrderStatus.Cancelled)
            .ToListAsync();
        var categories = await _db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var stats = categories.Select(category =>
        {
            var categoryLines = lines.Where(l => l.CategoryId == category.Id).ToList();
            return new CategoryStat
            {
                Category = category.Name,
                Slug = category.Slug,
                Revenue = categoryLines.Sum(l => l.LineTotal),
                Units = categoryLines.Sum(l => l.Quantity),
                OrderCount = categoryLines.Select(l => l.OrderId).Distinct().Count()
            };
        }).ToList();

        var recent = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Store)
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAt)
            .Take(8)
            .ToListAsync();

        var scheduled = await _db.Installments
            .Where(i => i.Status == InstallmentStatus.Scheduled && i.Order!.Status != OrderStatus.Cancelled)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;

        return View(new AdminDashboardView
        {
            Revenue = live.Sum(o => o.Total),
            OrderCount = live.Count,
            PendingVendors = await _db.Stores.CountAsync(s => s.ApprovalStatus == StoreApprovalStatus.Pending),
            StoreCount = await _db.Stores.CountAsync(s => s.ApprovalStatus == StoreApprovalStatus.Approved),
            ScheduledBnpl = scheduled,
            Categories = stats,
            RecentOrders = recent
        });
    }

    public async Task<IActionResult> Vendors()
    {
        var stores = await _db.Stores
            .Include(s => s.Owner)
            .OrderBy(s => s.ApprovalStatus)
            .ThenBy(s => s.Name)
            .ToListAsync();
        return View(stores);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id) => await SetStatus(id, StoreApprovalStatus.Approved, "approved");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id) => await SetStatus(id, StoreApprovalStatus.Rejected, "rejected");

    public async Task<IActionResult> Orders()
    {
        var orders = await _db.Orders
            .Include(o => o.Store)
            .Include(o => o.Customer)
            .Include(o => o.Payment)
            .Include(o => o.Lines)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View(orders);
    }

    public async Task<IActionResult> Order(int id)
    {
        var order = await _db.Orders
            .Include(o => o.Store)
            .Include(o => o.Customer)
            .Include(o => o.Payment)
            .Include(o => o.Lines)
            .Include(o => o.Installments)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();
        return View(order);
    }

    private async Task<IActionResult> SetStatus(int id, StoreApprovalStatus status, string verb)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == id);
        if (store is null)
            return NotFound();
        store.ApprovalStatus = status;
        await _db.SaveChangesAsync();
        TempData["Success"] = store.Name + " was " + verb + ".";
        return RedirectToAction(nameof(Vendors));
    }
}
