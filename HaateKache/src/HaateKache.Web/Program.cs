using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var mapsKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
if (!string.IsNullOrWhiteSpace(mapsKey))
    builder.Configuration["Maps:GoogleApiKey"] = mapsKey.Trim();

var stripeKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
if (!string.IsNullOrWhiteSpace(stripeKey))
    builder.Configuration["Stripe:SecretKey"] = stripeKey.Trim();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=haatekache.db";

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.Cookie.Name = "HaateKache.Auth";
    options.Cookie.HttpOnly = true;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "HaateKache.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(12);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("stripe", client =>
{
    client.BaseAddress = new Uri("https://api.stripe.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CartCountFilter>();
builder.Services.AddScoped<DemoPaymentGateway>();
builder.Services.AddScoped<StripeTestPaymentGateway>();
builder.Services.AddScoped<IPaymentGateway>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var kind = PaymentGatewaySelector.Select(config["Stripe:SecretKey"]);
    return kind == PaymentGatewayKind.StripeTest
        ? sp.GetRequiredService<StripeTestPaymentGateway>()
        : sp.GetRequiredService<DemoPaymentGateway>();
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<CartCountFilter>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
