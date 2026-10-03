using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private readonly CartService _cart;
    private readonly ApplicationDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        CartService cart,
        ApplicationDbContext db,
        IPaymentGateway gateway,
        UserManager<ApplicationUser> users,
        ILogger<CheckoutController> logger)
    {
        _cart = cart;
        _db = db;
        _gateway = gateway;
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var page = await BuildPageAsync(new CheckoutForm());
        if (page is null)
            return RedirectToAction("Index", "Cart");
        return View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CheckoutForm form)
    {
        var cardNumber = form.CardNumber;
        var cvc = form.Cvc;
        form.CardNumber = "";
        form.Cvc = "";
        ModelState.Remove(nameof(form.CardNumber));
        ModelState.Remove(nameof(form.Cvc));

        var page = await BuildPageAsync(form);
        if (page is null)
            return RedirectToAction("Index", "Cart");

        var error = Validate(form, cardNumber, cvc);
        if (error is not null)
        {
            page.Error = error;
            return View(page);
        }

        if (!CardRules.TryParseExpiry(form.Expiry, out var month, out var year))
        {
            page.Error = "Enter a future expiry as MM/YY.";
            return View(page);
        }

        var user = await _users.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var created = new List<int>();
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var group in page.Groups)
            {
                var ids = group.Lines.Select(l => l.ProductId).ToList();
                var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync();
                foreach (var line in group.Lines)
                {
                    var product = products.FirstOrDefault(p => p.Id == line.ProductId);
                    if (product is null || !product.IsActive || product.Stock < line.Quantity)
                    {
                        var stockMessage = (product?.Name ?? "An item") + " does not have enough stock. Your basket was not charged.";
                        await tx.RollbackAsync();
                        _db.ChangeTracker.Clear();
                        var refreshed = await BuildPageAsync(form);
                        if (refreshed is null)
                            return RedirectToAction("Index", "Cart");
                        refreshed.Error = stockMessage;
                        return View(refreshed);
                    }
                }

                var total = group.Lines.Sum(l => l.UnitPrice * l.Quantity);
                var method = form.PaymentMethod == "Bnpl" ? PaymentMethod.BuyNowPayLater : PaymentMethod.Card;
                var parts = method == PaymentMethod.BuyNowPayLater
                    ? InstallmentSchedule.Split(total, form.InstallmentCount)
                    : null;
                var charge = parts is null ? total : parts[0];
                var reference = "hk-" + user.Id + "-s" + group.StoreId + "-" + DateTime.UtcNow.Ticks;

                var auth = await _gateway.AuthorizeAsync(new CardAuthorizationRequest
                {
                    Amount = charge,
                    Currency = "bdt",
                    CardholderName = form.CardholderName.Trim(),
                    CardNumber = cardNumber,
                    ExpiryMonth = month,
                    ExpiryYear = year,
                    Cvc = cvc.Trim(),
                    Reference = reference
                }, HttpContext.RequestAborted);

                if (!auth.Approved)
                {
                    await tx.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    page.Error = string.IsNullOrWhiteSpace(auth.Message)
                        ? "The payment was declined."
                        : auth.Message;
                    return View(page);
                }

                var order = new Order
                {
                    OrderNumber = await NextOrderNumberAsync(),
                    CustomerId = user.Id,
                    StoreId = group.StoreId,
                    Status = OrderStatus.Pending,
                    Subtotal = total,
                    Total = total,
                    PaymentMethod = method,
                    CreatedAt = DateTime.UtcNow,
                    Payment = new Payment
                    {
                        Gateway = auth.Gateway,
                        Status = "Authorized",
                        AuthorizationCode = auth.AuthorizationCode,
                        GatewayReference = auth.GatewayReference,
                        CardBrand = auth.CardBrand,
                        CardLast4 = auth.CardLast4,
                        AmountAuthorized = charge,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                foreach (var line in group.Lines)
                {
                    var product = products.First(p => p.Id == line.ProductId);
                    product.Stock -= line.Quantity;
                    order.Lines.Add(new OrderLine
                    {
                        ProductId = product.Id,
                        ProductName = line.Name,
                        CategoryName = line.CategoryName,
                        CategoryId = line.CategoryId,
                        UnitPrice = line.UnitPrice,
                        Quantity = line.Quantity,
                        LineTotal = line.UnitPrice * line.Quantity
                    });
                }

                if (parts is not null)
                {
                    var interval = InstallmentSchedule.IntervalDays(form.InstallmentCount);
                    var today = DateTime.UtcNow.Date;
                    for (var i = 0; i < parts.Count; i++)
                    {
                        order.Installments.Add(new Installment
                        {
                            Sequence = i + 1,
                            Amount = parts[i],
                            DueDate = today.AddDays(interval * i),
                            Status = i == 0 ? InstallmentStatus.Authorized : InstallmentStatus.Scheduled,
                            PaidAt = i == 0 ? DateTime.UtcNow : null,
                            AuthorizationCode = i == 0 ? auth.AuthorizationCode : null
                        });
                    }
                }

                _db.Orders.Add(order);
                await _db.SaveChangesAsync();
                created.Add(order.Id);
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Checkout failed for user {UserId}", user.Id);
            page.Error = "We could not place the order. Nothing was saved.";
            return View(page);
        }

        _cart.Clear();
        return RedirectToAction(nameof(Confirmation), new { ids = string.Join(',', created) });
    }

    public async Task<IActionResult> Confirmation(string? ids)
    {
        var idList = ParseIds(ids);
        var userId = _users.GetUserId(User);
        var orders = await OrderQuery()
            .Where(o => idList.Contains(o.Id) && o.CustomerId == userId)
            .ToListAsync();
        if (orders.Count == 0)
            return RedirectToAction("Index", "Orders");
        return View(orders);
    }

    private async Task<CheckoutPageView?> BuildPageAsync(CheckoutForm form)
    {
        var lines = await _cart.GetLinesAsync();
        if (lines.Count == 0)
            return null;

        var storeIds = lines.Select(l => l.StoreId).Distinct().ToList();
        var stores = await _db.Stores.Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var groups = lines
            .GroupBy(l => l.StoreId)
            .Select(group =>
            {
                var store = stores[group.Key];
                var groupLines = group.ToList();
                return new CheckoutStoreGroup
                {
                    StoreId = store.Id,
                    StoreName = store.Name,
                    Address = store.Address,
                    Area = store.Area,
                    DistanceKm = groupLines[0].DistanceKm,
                    Lines = groupLines,
                    Subtotal = groupLines.Sum(l => l.LineTotal)
                };
            })
            .OrderBy(g => g.DistanceKm)
            .ToList();

        if (form.InstallmentCount is not (3 or 4))
            form.InstallmentCount = 3;
        if (form.PaymentMethod is not ("Card" or "Bnpl"))
            form.PaymentMethod = "Card";

        return new CheckoutPageView
        {
            Groups = groups,
            Total = groups.Sum(g => g.Subtotal),
            Form = new CheckoutForm
            {
                PaymentMethod = form.PaymentMethod,
                InstallmentCount = form.InstallmentCount,
                CardholderName = form.CardholderName,
                Expiry = form.Expiry
            }
        };
    }

    private static string? Validate(CheckoutForm form, string cardNumber, string cvc)
    {
        if (string.IsNullOrWhiteSpace(form.CardholderName))
            return "Enter the name on the card.";
        if (string.IsNullOrWhiteSpace(cardNumber))
            return "Enter the card number.";
        if (string.IsNullOrWhiteSpace(form.Expiry))
            return "Enter the expiry as MM/YY.";
        if (string.IsNullOrWhiteSpace(cvc))
            return "Enter the security code.";
        if (form.PaymentMethod == "Bnpl" && form.InstallmentCount is not (3 or 4))
            return "Choose 3 monthly or 4 fortnightly installments.";
        return null;
    }

    private async Task<string> NextOrderNumberAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var number = "HK-" + DateTime.UtcNow.ToString("yyMMdd") + "-" + Random.Shared.Next(1000, 9999);
            if (!await _db.Orders.AnyAsync(o => o.OrderNumber == number))
                return number;
        }

        return "HK-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private IQueryable<Order> OrderQuery() =>
        _db.Orders
            .Include(o => o.Store)
            .Include(o => o.Lines)
            .Include(o => o.Payment)
            .Include(o => o.Installments)
            .Include(o => o.Customer);

    private static List<int> ParseIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return new List<int>();
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
    }
}
