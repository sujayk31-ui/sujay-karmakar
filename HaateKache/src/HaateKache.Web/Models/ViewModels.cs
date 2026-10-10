namespace HaateKache.Web.Models;

public class ShopperLocation
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Label { get; set; } = "";

    public static ShopperLocation Default { get; } = new()
    {
        Latitude = 23.7516,
        Longitude = 90.3938,
        Label = "Karwan Bazar"
    };
}

public class HomeViewModel
{
    public ShopperLocation Location { get; set; } = ShopperLocation.Default;
    public List<Category> Categories { get; set; } = new();
    public List<StoreDistanceView> NearestStores { get; set; } = new();
    public List<ProductCardView> Featured { get; set; } = new();
}

public class StoreDistanceView
{
    public Store Store { get; set; } = null!;
    public double DistanceKm { get; set; }
    public bool IsNearest { get; set; }
}

public class ProductCardView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Unit { get; set; } = "";
    public int Stock { get; set; }
    public string CategoryName { get; set; } = "";
    public string CategorySlug { get; set; } = "";
    public string StoreName { get; set; } = "";
    public int StoreId { get; set; }
    public string Area { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double DistanceKm { get; set; }
    public string OpeningHours { get; set; } = "";
}

public class CategoryPageView
{
    public Category Category { get; set; } = null!;
    public List<ProductCardView> Products { get; set; } = new();
    public ShopperLocation Location { get; set; } = ShopperLocation.Default;
}

public class SearchPageView
{
    public string Query { get; set; } = "";
    public List<ProductCardView> Products { get; set; } = new();
    public ShopperLocation Location { get; set; } = ShopperLocation.Default;
}

public class StoreListView
{
    public ShopperLocation Location { get; set; } = ShopperLocation.Default;
    public List<StoreDistanceView> Stores { get; set; } = new();
    public MapViewModel Map { get; set; } = new();
}

public class StoreDetailView
{
    public StoreDistanceView Store { get; set; } = null!;
    public List<ProductCardView> Products { get; set; } = new();
    public MapViewModel Map { get; set; } = new();
    public ShopperLocation Location { get; set; } = ShopperLocation.Default;
}

public class MapViewModel
{
    public string PayloadJson { get; set; } = "[]";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? GoogleMapsApiKey { get; set; }
}

public class CartPageView
{
    public List<CartLine> Lines { get; set; } = new();
    public decimal Total => Lines.Sum(l => l.LineTotal);
}

public class CartLine
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public string Area { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int CategoryId { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int Stock { get; set; }
    public string Unit { get; set; } = "";
    public double DistanceKm { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class CheckoutPageView
{
    public List<CheckoutStoreGroup> Groups { get; set; } = new();
    public decimal Total { get; set; }
    public CheckoutForm Form { get; set; } = new();
    public string? Error { get; set; }
}

public class CheckoutStoreGroup
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public string Address { get; set; } = "";
    public string Area { get; set; } = "";
    public double DistanceKm { get; set; }
    public List<CartLine> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
}

public class CheckoutForm
{
    public string PaymentMethod { get; set; } = "Card";
    public int InstallmentCount { get; set; } = 3;
    public string CardholderName { get; set; } = "";
    public string CardNumber { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string Cvc { get; set; } = "";
}

public class LoginViewModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

public class RegisterViewModel
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public string? Error { get; set; }
}

public class VendorRegisterViewModel
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
    public string Area { get; set; } = "Dhanmondi";
    public double Latitude { get; set; } = 23.7556;
    public double Longitude { get; set; } = 90.3760;
    public string OpeningHours { get; set; } = "9:00–21:00";
    public string? Error { get; set; }
}

public class VendorDashboardView
{
    public Store Store { get; set; } = null!;
    public int ProductCount { get; set; }
    public int OpenOrders { get; set; }
    public decimal Revenue { get; set; }
    public List<Order> RecentOrders { get; set; } = new();
}

public class ProductForm
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Unit { get; set; } = "pc";
    public bool IsActive { get; set; } = true;
    public string? Error { get; set; }
    public List<Category> Categories { get; set; } = new();
}

public class AdminDashboardView
{
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
    public int PendingVendors { get; set; }
    public int StoreCount { get; set; }
    public decimal ScheduledBnpl { get; set; }
    public List<CategoryStat> Categories { get; set; } = new();
    public List<Order> RecentOrders { get; set; } = new();
}

public class CategoryStat
{
    public string Category { get; set; } = "";
    public string Slug { get; set; } = "";
    public int OrderCount { get; set; }
    public int Units { get; set; }
    public decimal Revenue { get; set; }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
