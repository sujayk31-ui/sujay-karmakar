using System.Globalization;
using System.Text.Json;
using HaateKache.Web.Data;
using HaateKache.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Services;

public static class Geo
{
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthKm = 6371.0;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthKm * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

public static class Neighborhoods
{
    public static IReadOnlyList<ShopperLocation> All { get; } =
    [
        new() { Label = "Gulshan", Latitude = 23.7948, Longitude = 90.4143 },
        new() { Label = "Banani", Latitude = 23.7937, Longitude = 90.4066 },
        new() { Label = "Dhanmondi", Latitude = 23.7556, Longitude = 90.3760 },
        new() { Label = "Mohammadpur", Latitude = 23.7658, Longitude = 90.3584 },
        new() { Label = "Mirpur", Latitude = 23.8069, Longitude = 90.3686 },
        new() { Label = "Uttara", Latitude = 23.8689, Longitude = 90.3900 },
        new() { Label = "Karwan Bazar", Latitude = 23.7516, Longitude = 90.3938 },
        new() { Label = "Lalmatia", Latitude = 23.7564, Longitude = 90.3675 }
    ];

    public static ShopperLocation? Find(string? label) =>
        All.FirstOrDefault(n => n.Label.Equals(label?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class Money
{
    public static string Taka(decimal value) =>
        "৳" + value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}

public static class DhakaTime
{
    public static string Format(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc.ToUniversalTime();
        return value.AddHours(6).ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) + " Dhaka";
    }

    public static string FormatDate(DateTime date) =>
        date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}

public static class StatusText
{
    public static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Pending",
        OrderStatus.Accepted => "Accepted",
        OrderStatus.ReadyForPickup => "Ready for pickup",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string Approval(StoreApprovalStatus status) => status switch
    {
        StoreApprovalStatus.Pending => "Pending approval",
        StoreApprovalStatus.Approved => "Approved",
        StoreApprovalStatus.Rejected => "Rejected",
        _ => status.ToString()
    };

    public static string Installment(InstallmentStatus status) => status switch
    {
        InstallmentStatus.Scheduled => "Scheduled",
        InstallmentStatus.Authorized => "Authorized",
        InstallmentStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string PayMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => "Card",
        PaymentMethod.BuyNowPayLater => "Buy now, pay later",
        _ => method.ToString()
    };
}

public static class CatalogMap
{
    public static IQueryable<Product> LiveProducts(ApplicationDbContext db) =>
        db.Products
            .Include(p => p.Store)
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.Store!.ApprovalStatus == StoreApprovalStatus.Approved);

    public static ProductCardView ToCard(Product product, ShopperLocation location)
    {
        var store = product.Store!;
        var category = product.Category!;
        return new ProductCardView
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            Unit = product.Unit,
            Stock = product.Stock,
            CategoryName = category.Name,
            CategorySlug = category.Slug,
            StoreName = store.Name,
            StoreId = store.Id,
            Area = store.Area,
            Address = store.Address,
            Latitude = store.Latitude,
            Longitude = store.Longitude,
            OpeningHours = store.OpeningHours,
            DistanceKm = Geo.HaversineKm(location.Latitude, location.Longitude, store.Latitude, store.Longitude)
        };
    }
}

public class LocationService
{
    private const string SessionKey = "shopper-location";
    private readonly IHttpContextAccessor _http;

    public LocationService(IHttpContextAccessor http) => _http = http;

    public ShopperLocation Get()
    {
        var raw = _http.HttpContext?.Session.GetString(SessionKey);
        if (!string.IsNullOrEmpty(raw))
        {
            var parsed = JsonSerializer.Deserialize<ShopperLocation>(raw);
            if (parsed is not null && parsed.Latitude is >= -90 and <= 90 && parsed.Longitude is >= -180 and <= 180)
                return parsed;
        }

        return ShopperLocation.Default;
    }

    public void Set(ShopperLocation location)
    {
        _http.HttpContext?.Session.SetString(SessionKey, JsonSerializer.Serialize(location));
    }
}

public class CartSessionItem
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class CartService
{
    private const string SessionKey = "cart-v1";
    private readonly IHttpContextAccessor _http;
    private readonly ApplicationDbContext _db;
    private readonly LocationService _location;

    public CartService(IHttpContextAccessor http, ApplicationDbContext db, LocationService location)
    {
        _http = http;
        _db = db;
        _location = location;
    }

    public IReadOnlyList<CartSessionItem> GetItems()
    {
        var raw = _http.HttpContext?.Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(raw))
            return Array.Empty<CartSessionItem>();
        return JsonSerializer.Deserialize<List<CartSessionItem>>(raw) ?? new List<CartSessionItem>();
    }

    public int GetCount() => GetItems().Sum(i => i.Quantity);

    public void Add(int productId, int quantity)
    {
        var items = GetItems().ToList();
        var existing = items.FirstOrDefault(i => i.ProductId == productId);
        var add = Math.Clamp(quantity, 1, 20);
        if (existing is null)
            items.Add(new CartSessionItem { ProductId = productId, Quantity = add });
        else
            existing.Quantity = Math.Min(existing.Quantity + add, 20);
        Save(items);
    }

    public void SetQuantity(int productId, int quantity)
    {
        var items = GetItems().Where(i => i.ProductId != productId).ToList();
        if (quantity > 0)
            items.Add(new CartSessionItem { ProductId = productId, Quantity = Math.Min(quantity, 20) });
        Save(items);
    }

    public void Clear() => _http.HttpContext?.Session.Remove(SessionKey);

    public async Task<List<CartLine>> GetLinesAsync()
    {
        var items = GetItems();
        if (items.Count == 0)
            return new List<CartLine>();

        var ids = items.Select(i => i.ProductId).ToList();
        var products = await _db.Products
            .Include(p => p.Store)
            .Include(p => p.Category)
            .Where(p => ids.Contains(p.Id) && p.IsActive && p.Store!.ApprovalStatus == StoreApprovalStatus.Approved)
            .ToListAsync();

        var location = _location.Get();
        var lines = new List<CartLine>();
        foreach (var item in items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product?.Store is null || product.Category is null || product.Stock <= 0)
                continue;

            lines.Add(new CartLine
            {
                ProductId = product.Id,
                Name = product.Name,
                StoreId = product.StoreId,
                StoreName = product.Store.Name,
                Area = product.Store.Area,
                CategoryName = product.Category.Name,
                CategoryId = product.CategoryId,
                UnitPrice = product.Price,
                Quantity = Math.Min(item.Quantity, product.Stock),
                Stock = product.Stock,
                Unit = product.Unit,
                DistanceKm = Geo.HaversineKm(location.Latitude, location.Longitude, product.Store.Latitude, product.Store.Longitude)
            });
        }

        return lines.OrderBy(l => l.StoreName).ThenBy(l => l.Name).ToList();
    }

    private void Save(List<CartSessionItem> items)
    {
        _http.HttpContext?.Session.SetString(SessionKey, JsonSerializer.Serialize(items));
    }
}

public class CartCountFilter : IAsyncActionFilter
{
    private readonly CartService _cart;

    public CartCountFilter(CartService cart) => _cart = cart;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller is Controller controller)
            controller.ViewData["CartCount"] = _cart.GetCount();
        await next();
    }
}
