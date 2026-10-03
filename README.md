# Haate Kache

Haate Kache (হাতে কাছে, “close at hand”) is a hyperlocal pickup shop for Dhaka. Shoppers browse groceries, stationery, jewelry, and clothing from neighborhood stores, see which pickup point is nearest, and pay with a demo card or buy-now-pay-later installments.

The ASP.NET Core app lives in [`HaateKache/`](HaateKache/). A separate Flask asset tracker already in this repository (`app.py`, `main.py`) is unchanged.

## Run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then:

```bash
cd HaateKache
dotnet build
dotnet run --project src/HaateKache.Web --urls http://localhost:5080
```

Open http://localhost:5080. The first launch creates `HaateKache/src/HaateKache.Web/haatekache.db` (SQLite) and seeds stores, products, and sample orders. Delete that file to seed again.

```bash
dotnet test HaateKache/HaateKache.sln
```

## Demo accounts

| Who | Email | Password |
| --- | --- | --- |
| Owner (admin) | owner@haatekache.bd | Owner#12345 |
| Shopper | customer@haatekache.bd | Shopper#12345 |
| Gulshan Haat Mart | vendor.gulshan@haatekache.bd | Vendor#12345 |
| Dhanmondi Paper Lane | vendor.dhanmondi@haatekache.bd | Vendor#12345 |
| Mirpur Rupa Jewelers | vendor.mirpur@haatekache.bd | Vendor#12345 |
| Uttara Loom & Cloth | vendor.uttara@haatekache.bd | Vendor#12345 |
| Banani Daily Basket | vendor.banani@haatekache.bd | Vendor#12345 |
| Mohammadpur Para Store | vendor.mohammadpur@haatekache.bd | Vendor#12345 |
| Pending store (Lalmatia) | vendor.pending@haatekache.bd | Vendor#12345 |

Distances start from Karwan Bazar until you pick another neighborhood or use browser location.

## What to try

1. Browse a category, add items to the basket, and check out. Use card `4242 4242 4242 4242`, any future expiry, and any 3-digit CVC. `4000 0000 0000 0002` is declined.
2. Check out again with **Buy now, pay later** (3 monthly or 4 fortnightly payments). The schedule is on the shopper order, the store order, and the owner order.
3. Register a store at `/Account/VendorRegister`. Sign in as the owner and approve it under **Vendor registrations**. The vendor can then sign in and manage products.
4. Sign in as `vendor.gulshan@haatekache.bd` and move an order through accept, ready for pickup, and completed (or cancel it).
5. Open **Owner** for sales revenue and orders by category.

## Payments

Checkout goes through `IPaymentGateway`. The default `Demo` gateway authorizes test cards in-process and does not call a payment processor. Only the brand, last four digits, amount, and authorization code are stored. The card number and CVC are not columns in the database and are not written to logs.

If `STRIPE_SECRET_KEY` (or `Stripe:SecretKey`) is an `sk_test_…` key, the Stripe test gateway is selected instead. Live keys (`sk_live_…`) are ignored. Leave the variable unset for the demo.

## Maps

Stores have latitude and longitude. Nearest pickup uses the haversine distance from the shopper’s selected point.

- No key: Leaflet and OpenStreetMap tiles (files under `wwwroot/lib/leaflet`).
- `GOOGLE_MAPS_API_KEY` (or `Maps:GoogleApiKey`): Google Maps JavaScript. Do not commit a key. The checked-in setting is empty.

## Stack

ASP.NET Core MVC and Razor, ASP.NET Core Identity, EF Core, SQLite. Roles are Admin, Vendor, and Customer. A vendor only sees their own store’s products and orders.
