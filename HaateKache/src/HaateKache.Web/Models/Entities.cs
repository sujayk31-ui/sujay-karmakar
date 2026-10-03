using Microsoft.AspNetCore.Identity;

namespace HaateKache.Web.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
    public string Area { get; set; } = "";
    public string City { get; set; } = "Dhaka";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Phone { get; set; } = "";
    public string OpeningHours { get; set; } = "9:00–21:00";
    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }
    public StoreApprovalStatus ApprovalStatus { get; set; } = StoreApprovalStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public enum StoreApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
}

public class Product
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Unit { get; set; } = "pc";
    public string Sku { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public ApplicationUser? Customer { get; set; }
    public int StoreId { get; set; }
    public Store? Store { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public Payment? Payment { get; set; }
    public ICollection<Installment> Installments { get; set; } = new List<Installment>();
}

public enum OrderStatus
{
    Pending = 0,
    Accepted = 1,
    ReadyForPickup = 2,
    Completed = 3,
    Cancelled = 4
}

public enum PaymentMethod
{
    Card = 0,
    BuyNowPayLater = 1
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int CategoryId { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>
/// Gateway result stored for an order. The full card number and CVC are never persisted.
/// </summary>
public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string Gateway { get; set; } = "Demo";
    public string Status { get; set; } = "";
    public string? AuthorizationCode { get; set; }
    public string? GatewayReference { get; set; }
    public string CardBrand { get; set; } = "";
    public string CardLast4 { get; set; } = "";
    public decimal AmountAuthorized { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Installment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int Sequence { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public InstallmentStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? AuthorizationCode { get; set; }
}

public enum InstallmentStatus
{
    Scheduled = 0,
    Authorized = 1,
    Cancelled = 2
}
