using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

[Route("stores")]
public class StoresController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly LocationService _location;
    private readonly IConfiguration _config;

    public StoresController(ApplicationDbContext db, LocationService location, IConfiguration config)
    {
        _db = db;
        _location = location;
        _config = config;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var location = _location.Get();
        var stores = await _db.Stores
            .Where(s => s.ApprovalStatus == StoreApprovalStatus.Approved)
            .ToListAsync();
        var ranked = HomeController.Rank(stores, location);
        return View(new StoreListView
        {
            Location = location,
            Stores = ranked,
            Map = MapFactory.Build(_config, location, ranked)
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == id && s.ApprovalStatus == StoreApprovalStatus.Approved);
        if (store is null)
            return NotFound();

        var location = _location.Get();
        var ranked = HomeController.Rank(new[] { store }, location);
        var products = await CatalogMap.LiveProducts(_db)
            .Where(p => p.StoreId == store.Id && p.Stock > 0)
            .ToListAsync();

        return View(new StoreDetailView
        {
            Store = ranked[0],
            Location = location,
            Products = products.Select(p => CatalogMap.ToCard(p, location)).OrderBy(p => p.CategoryName).ThenBy(p => p.Name).ToList(),
            Map = MapFactory.Build(_config, location, ranked)
        });
    }
}
