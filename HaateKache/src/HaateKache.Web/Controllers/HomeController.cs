using System.Diagnostics;
using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly LocationService _location;

    public HomeController(ApplicationDbContext db, LocationService location)
    {
        _db = db;
        _location = location;
    }

    public async Task<IActionResult> Index()
    {
        var location = _location.Get();
        var categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        var stores = await _db.Stores
            .Where(s => s.ApprovalStatus == StoreApprovalStatus.Approved)
            .ToListAsync();
        var nearest = Rank(stores, location).Take(4).ToList();
        var products = await CatalogMap.LiveProducts(_db).Where(p => p.Stock > 0).ToListAsync();
        var featured = products
            .Select(p => CatalogMap.ToCard(p, location))
            .OrderBy(p => p.DistanceKm)
            .ThenBy(p => p.Name)
            .Take(8)
            .ToList();

        return View(new HomeViewModel
        {
            Location = location,
            Categories = categories,
            NearestStores = nearest,
            Featured = featured
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLocation(string? preset, double? latitude, double? longitude, string? label, string? returnUrl)
    {
        var known = Neighborhoods.Find(preset);
        ShopperLocation location;
        if (known is not null)
        {
            location = new ShopperLocation
            {
                Label = known.Label,
                Latitude = known.Latitude,
                Longitude = known.Longitude
            };
        }
        else if (latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
        {
            location = new ShopperLocation
            {
                Latitude = latitude.Value,
                Longitude = longitude.Value,
                Label = string.IsNullOrWhiteSpace(label) ? "Your location" : label.Trim()
            };
        }
        else
        {
            location = ShopperLocation.Default;
        }

        _location.Set(location);
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    internal static List<StoreDistanceView> Rank(IEnumerable<Store> stores, ShopperLocation location)
    {
        var ranked = stores
            .Select(store => new StoreDistanceView
            {
                Store = store,
                DistanceKm = Geo.HaversineKm(location.Latitude, location.Longitude, store.Latitude, store.Longitude)
            })
            .OrderBy(item => item.DistanceKm)
            .ToList();
        if (ranked.Count > 0)
            ranked[0].IsNearest = true;
        return ranked;
    }
}
