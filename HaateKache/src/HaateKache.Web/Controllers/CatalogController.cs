using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

[Route("shop")]
public class CatalogController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly LocationService _location;

    public CatalogController(ApplicationDbContext db, LocationService location)
    {
        _db = db;
        _location = location;
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Category(string slug)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == slug);
        if (category is null)
            return NotFound();

        var location = _location.Get();
        var products = await CatalogMap.LiveProducts(_db)
            .Where(p => p.CategoryId == category.Id && p.Stock > 0)
            .ToListAsync();

        return View(new CategoryPageView
        {
            Category = category,
            Location = location,
            Products = products.Select(p => CatalogMap.ToCard(p, location)).OrderBy(p => p.DistanceKm).ThenBy(p => p.Price).ToList()
        });
    }

    [HttpGet("product/{id:int}")]
    public async Task<IActionResult> Product(int id)
    {
        var product = await CatalogMap.LiveProducts(_db).FirstOrDefaultAsync(p => p.Id == id);
        if (product is null)
            return NotFound();

        return View(CatalogMap.ToCard(product, _location.Get()));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q)
    {
        var query = (q ?? "").Trim();
        var location = _location.Get();
        var products = CatalogMap.LiveProducts(_db).Where(p => p.Stock > 0);
        if (query.Length > 0)
        {
            var term = query.ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Description.ToLower().Contains(term) ||
                p.Store!.Name.ToLower().Contains(term) ||
                p.Store.Area.ToLower().Contains(term) ||
                p.Category!.Name.ToLower().Contains(term));
        }

        var list = await products.ToListAsync();
        return View(new SearchPageView
        {
            Query = query,
            Location = location,
            Products = list.Select(p => CatalogMap.ToCard(p, location)).OrderBy(p => p.DistanceKm).ToList()
        });
    }
}
