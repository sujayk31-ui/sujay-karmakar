using System.Reflection;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HaateKache.Tests;

public class PlatformTests
{
    [Fact]
    public void Same_point_is_zero_kilometers()
    {
        var km = Geo.HaversineKm(23.7516, 90.3938, 23.7516, 90.3938);
        Assert.Equal(0, km, 3);
    }

    [Fact]
    public void Gulshan_is_closer_to_Banani_than_to_Uttara()
    {
        var gulshanLat = 23.7948;
        var gulshanLng = 90.4143;
        var banani = Geo.HaversineKm(gulshanLat, gulshanLng, 23.7937, 90.4066);
        var uttara = Geo.HaversineKm(gulshanLat, gulshanLng, 23.8689, 90.3900);
        Assert.InRange(banani, 0.2, 2.5);
        Assert.True(banani < uttara);
    }

    [Fact]
    public void Installments_sum_to_the_order_total()
    {
        var parts = InstallmentSchedule.Split(3400m, 3);
        Assert.Equal(3, parts.Count);
        Assert.Equal(3400m, parts.Sum());
        Assert.All(parts, part => Assert.Equal(part, decimal.Round(part, 2)));
    }

    [Theory]
    [InlineData(null, PaymentGatewayKind.Demo)]
    [InlineData("", PaymentGatewayKind.Demo)]
    [InlineData("sk_live_secret", PaymentGatewayKind.Demo)]
    [InlineData("pk_test_not_a_secret", PaymentGatewayKind.Demo)]
    [InlineData("sk_test_demo", PaymentGatewayKind.StripeTest)]
    public void Gateway_selection_uses_only_a_stripe_test_secret(string? key, PaymentGatewayKind expected)
    {
        Assert.Equal(expected, PaymentGatewaySelector.Select(key));
    }

    [Fact]
    public async Task Demo_authorization_keeps_only_the_last_four()
    {
        var gateway = new DemoPaymentGateway(NullLogger<DemoPaymentGateway>.Instance);
        const string pan = "4242424242424242";
        var result = await gateway.AuthorizeAsync(new CardAuthorizationRequest
        {
            Amount = 120.50m,
            CardholderName = "Farhan Ahmed",
            CardNumber = pan,
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Cvc = "123",
            Reference = "test"
        }, CancellationToken.None);

        Assert.True(result.Approved);
        Assert.Equal("4242", result.CardLast4);
        Assert.Equal("Visa", result.CardBrand);
        Assert.Equal("Demo", result.Gateway);
        Assert.DoesNotContain(pan, result.AuthorizationCode);
        foreach (var property in result.GetType().GetProperties())
        {
            var value = property.GetValue(result)?.ToString() ?? "";
            Assert.DoesNotContain(pan, value);
        }
    }

    [Fact]
    public async Task Known_decline_card_is_not_authorized()
    {
        var gateway = new DemoPaymentGateway(NullLogger<DemoPaymentGateway>.Instance);
        var result = await gateway.AuthorizeAsync(new CardAuthorizationRequest
        {
            Amount = 50m,
            CardholderName = "Farhan Ahmed",
            CardNumber = "4000000000000002",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Cvc = "123",
            Reference = "decline"
        }, CancellationToken.None);

        Assert.False(result.Approved);
        Assert.Equal("The card was declined.", result.Message);
    }

    [Fact]
    public void Payment_record_has_no_place_for_a_raw_card()
    {
        var names = typeof(Payment).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.Contains("CardLast4", names);
        Assert.DoesNotContain(names, name => name.Contains("Number", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Cvc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Pan", StringComparison.OrdinalIgnoreCase));
    }
}
