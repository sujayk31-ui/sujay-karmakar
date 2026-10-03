using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

public class CartController : Controller
{
    private readonly CartService _cart;
    private readonly ApplicationDbContext _db;

    public CartController(CartService cart, ApplicationDbContext db)
    {
        _cart = cart;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        return View(new CartPageView { Lines = await _cart.GetLinesAsync() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? returnUrl = null)
    {
        var product = await _db.Products
            .Include(p => p.Store)
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive && p.Stock > 0 && p.Store!.ApprovalStatus == StoreApprovalStatus.Approved);
        if (product is null)
        {
            TempData["Error"] = "That product is not available.";
            return RedirectToAction(nameof(Index));
        }

        _cart.Add(productId, quantity);
        TempData["Success"] = product.Name + " added to your basket.";
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Update(int productId, int quantity)
    {
        _cart.SetQuantity(productId, quantity);
        return RedirectToAction(nameof(Index));
    }
}
