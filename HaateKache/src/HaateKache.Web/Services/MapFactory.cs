using System.Text.Encodings.Web;
using System.Text.Json;
using HaateKache.Web.Models;

namespace HaateKache.Web.Services;

public static class MapFactory
{
    public static MapViewModel Build(IConfiguration config, ShopperLocation location, IEnumerable<StoreDistanceView> stores)
    {
        var payload = stores.Select(item => new
        {
            id = item.Store.Id,
            name = item.Store.Name,
            area = item.Store.Area,
            address = item.Store.Address,
            lat = item.Store.Latitude,
            lng = item.Store.Longitude,
            distance = Math.Round(item.DistanceKm, 1),
            hours = item.Store.OpeningHours
        });

        var key = config["Maps:GoogleApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            key = null;

        return new MapViewModel
        {
            PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Default
            }),
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            GoogleMapsApiKey = key
        };
    }
}
