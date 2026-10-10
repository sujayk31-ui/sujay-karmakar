using HaateKache.Web.Data;
using HaateKache.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public OrdersController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _users.GetUserId(User)!;
        var orders = await Query()
            .Where(o => o.CustomerId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var userId = _users.GetUserId(User)!;
        var order = await Query().FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == userId);
        if (order is null)
            return NotFound();
        return View(order);
    }

    private IQueryable<Order> Query() =>
        _db.Orders
            .Include(o => o.Store)
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .Include(o => o.Payment)
            .Include(o => o.Installments);
}
