using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HaateKache.Web.Services;

public enum PaymentGatewayKind
{
    Demo,
    StripeTest
}

public static class PaymentGatewaySelector
{
    public static PaymentGatewayKind Select(string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            return PaymentGatewayKind.Demo;

        var key = secretKey.Trim();
        if (key.StartsWith("sk_test_", StringComparison.Ordinal))
            return PaymentGatewayKind.StripeTest;

        // Live keys and anything else stay on the demo gateway. This app never charges a live processor.
        return PaymentGatewayKind.Demo;
    }
}

public sealed class CardAuthorizationRequest
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "bdt";
    public string CardholderName { get; init; } = "";
    public string CardNumber { get; init; } = "";
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public string Cvc { get; init; } = "";
    public string Reference { get; init; } = "";
}

public sealed class PaymentAuthorization
{
    public bool Approved { get; init; }
    public string Message { get; init; } = "";
    public string AuthorizationCode { get; init; } = "";
    public string? GatewayReference { get; init; }
    public string CardLast4 { get; init; } = "";
    public string CardBrand { get; init; } = "";
    public string Gateway { get; init; } = "";

    public static PaymentAuthorization Success(string code, string? reference, string last4, string brand, string gateway) =>
        new()
        {
            Approved = true,
            Message = "Authorized",
            AuthorizationCode = code,
            GatewayReference = reference,
            CardLast4 = last4,
            CardBrand = brand,
            Gateway = gateway
        };

    public static PaymentAuthorization Declined(string message, string gateway) =>
        new()
        {
            Approved = false,
            Message = message,
            Gateway = gateway
        };
}

public interface IPaymentGateway
{
    string Name { get; }
    Task<PaymentAuthorization> AuthorizeAsync(CardAuthorizationRequest request, CancellationToken cancellationToken);
}

public static class CardRules
{
    public static string Digits(string? value) =>
        new(value?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());

    public static bool Luhn(string digits)
    {
        if (digits.Length is < 13 or > 19)
            return false;

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (n is < 0 or > 9)
                return false;
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                    n -= 9;
            }

            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public static string Brand(string digits) => digits.Length == 0 ? "Card" : digits[0] switch
    {
        '4' => "Visa",
        '5' => "Mastercard",
        '3' => "American Express",
        '6' => "Discover",
        _ => "Card"
    };

    public static bool TryParseExpiry(string? expiry, out int month, out int year)
    {
        month = 0;
        year = 0;
        if (string.IsNullOrWhiteSpace(expiry))
            return false;

        var parts = expiry.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;
        if (!int.TryParse(parts[0], out month) || month is < 1 or > 12)
            return false;
        if (!int.TryParse(parts[1], out year))
            return false;
        if (year < 100)
            year += 2000;

        var lastDay = DateTime.DaysInMonth(year, month);
        var end = new DateTime(year, month, lastDay, 23, 59, 59, DateTimeKind.Utc);
        return end >= DateTime.UtcNow;
    }
}

public static class InstallmentSchedule
{
    public static IReadOnlyList<decimal> Split(decimal total, int count)
    {
        if (count is < 2 or > 6)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (total <= 0)
            throw new ArgumentOutOfRangeException(nameof(total));

        var baseAmount = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var parts = Enumerable.Repeat(baseAmount, count).ToArray();
        var drift = total - parts.Sum();
        parts[0] = Math.Round(parts[0] + drift, 2, MidpointRounding.AwayFromZero);
        return parts;
    }

    public static int IntervalDays(int count) => count == 4 ? 14 : 30;
}

public sealed class DemoPaymentGateway : IPaymentGateway
{
    private readonly ILogger<DemoPaymentGateway> _logger;

    public DemoPaymentGateway(ILogger<DemoPaymentGateway> logger) => _logger = logger;

    public string Name => "Demo";

    public Task<PaymentAuthorization> AuthorizeAsync(CardAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var digits = CardRules.Digits(request.CardNumber);
        var last4 = digits.Length >= 4 ? digits[^4..] : "";

        if (request.Amount <= 0)
            return Task.FromResult(PaymentAuthorization.Declined("Amount must be greater than zero.", Name));
        if (string.IsNullOrWhiteSpace(request.CardholderName))
            return Task.FromResult(PaymentAuthorization.Declined("Enter the name on the card.", Name));
        if (!CardRules.Luhn(digits))
            return Task.FromResult(PaymentAuthorization.Declined("That card number is not valid.", Name));
        if (request.Cvc.Length is < 3 or > 4 || !request.Cvc.All(char.IsDigit))
            return Task.FromResult(PaymentAuthorization.Declined("Enter the 3 or 4 digit security code.", Name));
        if (request.ExpiryMonth is < 1 or > 12 || request.ExpiryYear < DateTime.UtcNow.Year)
            return Task.FromResult(PaymentAuthorization.Declined("That card is expired.", Name));

        if (digits == "4000000000000002")
        {
            _logger.LogInformation("Demo authorization declined for ****{Last4}", last4);
            return Task.FromResult(PaymentAuthorization.Declined("The card was declined.", Name));
        }

        if (digits == "4000000000009995")
        {
            _logger.LogInformation("Demo authorization declined for ****{Last4}", last4);
            return Task.FromResult(PaymentAuthorization.Declined("The card has insufficient funds.", Name));
        }

        var code = "DEMO-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _logger.LogInformation("Demo authorization approved for ****{Last4} as {Code}", last4, code);
        return Task.FromResult(PaymentAuthorization.Success(code, code, last4, CardRules.Brand(digits), Name));
    }
}

/// <summary>
/// Optional Stripe test-mode hook. Selected only when STRIPE_SECRET_KEY starts with sk_test_.
/// Live keys are ignored. This class is not used unless that environment variable is present.
/// </summary>
public sealed class StripeTestPaymentGateway : IPaymentGateway
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeTestPaymentGateway> _logger;

    public StripeTestPaymentGateway(IHttpClientFactory httpFactory, IConfiguration config, ILogger<StripeTestPaymentGateway> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public string Name => "StripeTest";

    public async Task<PaymentAuthorization> AuthorizeAsync(CardAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var key = _config["Stripe:SecretKey"]?.Trim() ?? "";
        if (!key.StartsWith("sk_test_", StringComparison.Ordinal))
            return PaymentAuthorization.Declined("Stripe test mode is not configured.", Name);

        var digits = CardRules.Digits(request.CardNumber);
        var last4 = digits.Length >= 4 ? digits[^4..] : "";
        var amount = (long)Math.Round(request.Amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return PaymentAuthorization.Declined("Amount must be greater than zero.", Name);

        var fields = new Dictionary<string, string>
        {
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            ["currency"] = "bdt",
            ["confirm"] = "true",
            ["description"] = "Haate Kache " + request.Reference,
            ["payment_method_data[type]"] = "card",
            ["payment_method_data[card][number]"] = digits,
            ["payment_method_data[card][exp_month]"] = request.ExpiryMonth.ToString(CultureInfo.InvariantCulture),
            ["payment_method_data[card][exp_year]"] = request.ExpiryYear.ToString(CultureInfo.InvariantCulture),
            ["payment_method_data[card][cvc]"] = request.Cvc,
            ["payment_method_data[billing_details][name]"] = request.CardholderName
        };

        try
        {
            var client = _httpFactory.CreateClient("stripe");
            using var message = new HttpRequestMessage(HttpMethod.Post, "v1/payment_intents");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            message.Headers.TryAddWithoutValidation("Idempotency-Key", request.Reference + "-" + Guid.NewGuid().ToString("N"));
            message.Content = new FormUrlEncodedContent(fields);

            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Stripe test authorization declined with status {Status}", (int)response.StatusCode);
                return PaymentAuthorization.Declined("The card was declined by the test gateway.", Name);
            }

            using var json = JsonDocument.Parse(body);
            var id = json.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var status = json.RootElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            if (status is not ("succeeded" or "requires_capture" or "processing"))
                return PaymentAuthorization.Declined("The card was declined by the test gateway.", Name);

            return PaymentAuthorization.Success(id ?? request.Reference, id, last4, CardRules.Brand(digits), Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Stripe test authorization could not be completed.");
            return PaymentAuthorization.Declined("The test gateway could not be reached.", Name);
        }
    }
}
