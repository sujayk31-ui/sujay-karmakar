using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in new[] { "Admin", "Vendor", "Customer" })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }

        await EnsureUser(users, "owner@haatekache.bd", "Owner#12345", "Nabila Rahman", "Admin", "01700-000001");
        var customer = await EnsureUser(users, "customer@haatekache.bd", "Shopper#12345", "Farhan Ahmed", "Customer", "01700-000002");
        await EnsureUser(users, "vendor.gulshan@haatekache.bd", "Vendor#12345", "Shirin Akter", "Vendor", "01711-100001");
        await EnsureUser(users, "vendor.dhanmondi@haatekache.bd", "Vendor#12345", "Imran Hossain", "Vendor", "01711-100002");
        await EnsureUser(users, "vendor.mirpur@haatekache.bd", "Vendor#12345", "Rupa Begum", "Vendor", "01711-100003");
        await EnsureUser(users, "vendor.uttara@haatekache.bd", "Vendor#12345", "Tanvir Islam", "Vendor", "01711-100004");
        await EnsureUser(users, "vendor.banani@haatekache.bd", "Vendor#12345", "Nadia Karim", "Vendor", "01711-100005");
        await EnsureUser(users, "vendor.mohammadpur@haatekache.bd", "Vendor#12345", "Jamal Uddin", "Vendor", "01711-100006");
        await EnsureUser(users, "vendor.pending@haatekache.bd", "Vendor#12345", "Lina Chowdhury", "Vendor", "01711-100007");

        if (!await db.Categories.AnyAsync())
        {
            db.Categories.AddRange(
                new Category { Name = "Groceries", Slug = "groceries", Description = "Rice, oil, eggs, and the everyday basket from shops down the lane." },
                new Category { Name = "Stationery", Slug = "stationery", Description = "Paper, pens, and school kits without a trip across town." },
                new Category { Name = "Jewelry", Slug = "jewelry", Description = "Everyday gold-tone, silver, and bead pieces from neighborhood jewelers." },
                new Category { Name = "Clothing", Slug = "clothing", Description = "Punjabi, saree, shirts, and kidswear you can pick up today." });
            await db.SaveChangesAsync();
        }

        if (!await db.Stores.AnyAsync())
        {
            var owners = await db.Users.ToDictionaryAsync(u => u.Email ?? "", u => u.Id);
            foreach (var seed in StoreSeeds)
            {
                db.Stores.Add(new Store
                {
                    Name = seed.Name,
                    Slug = seed.Slug,
                    Description = seed.Description,
                    Address = seed.Address,
                    Area = seed.Area,
                    Latitude = seed.Latitude,
                    Longitude = seed.Longitude,
                    Phone = seed.Phone,
                    OpeningHours = seed.Hours,
                    OwnerId = owners[seed.OwnerEmail],
                    ApprovalStatus = seed.Approved ? StoreApprovalStatus.Approved : StoreApprovalStatus.Pending,
                    CreatedAt = DateTime.UtcNow.AddDays(-seed.DaysOld)
                });
            }

            await db.SaveChangesAsync();

            var stores = await db.Stores.ToDictionaryAsync(s => s.Slug);
            var categories = await db.Categories.ToDictionaryAsync(c => c.Slug);
            var sku = 100;
            foreach (var item in ProductSeeds)
            {
                sku++;
                db.Products.Add(new Product
                {
                    StoreId = stores[item.Store].Id,
                    CategoryId = categories[item.Category].Id,
                    Name = item.Name,
                    Description = item.Description,
                    Price = item.Price,
                    Stock = item.Stock,
                    Unit = item.Unit,
                    Sku = "HK-" + sku,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync();
        }

        if (!await db.Orders.AnyAsync())
            await SeedOrdersAsync(db, customer.Id);
    }

    private static async Task<ApplicationUser> EnsureUser(
        UserManager<ApplicationUser> users,
        string email,
        string password,
        string name,
        string role,
        string phone)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = name,
                PhoneNumber = phone,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException("Could not seed " + email + ": " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        if (!await users.IsInRoleAsync(user, role))
            await users.AddToRoleAsync(user, role);
        return user;
    }

    private static async Task SeedOrdersAsync(ApplicationDbContext db, string customerId)
    {
        var products = await db.Products.Include(p => p.Store).Include(p => p.Category).ToListAsync();

        Product Find(string storeSlug, string name) =>
            products.First(p => p.Store!.Slug == storeSlug && p.Name == name);

        Add(db, customerId, "HK-1001", OrderStatus.Completed, PaymentMethod.Card, 4, null,
            (Find("gulshan-haat-mart", "Miniket rice, 5 kg"), 1),
            (Find("gulshan-haat-mart", "Mustard oil, 1 L"), 1));

        Add(db, customerId, "HK-1002", OrderStatus.Completed, PaymentMethod.Card, 3, null,
            (Find("dhanmondi-paper-lane", "A4 copy paper, 500 sheets"), 1),
            (Find("dhanmondi-paper-lane", "Ballpoint pens, pack of 10"), 2));

        Add(db, customerId, "HK-1003", OrderStatus.Completed, PaymentMethod.BuyNowPayLater, 12, 3,
            (Find("mirpur-rupa-jewelers", "Kundan necklace"), 1));

        Add(db, customerId, "HK-1004", OrderStatus.Accepted, PaymentMethod.Card, 1, null,
            (Find("uttara-loom-cloth", "Cotton punjabi"), 1));

        Add(db, customerId, "HK-1005", OrderStatus.ReadyForPickup, PaymentMethod.Card, 1, null,
            (Find("banani-daily-basket", "Brown eggs, dozen"), 1),
            (Find("banani-daily-basket", "Family tea, 400 g"), 1));

        Add(db, customerId, "HK-1006", OrderStatus.Completed, PaymentMethod.Card, 6, null,
            (Find("mohammadpur-para-store", "Tangail cotton saree"), 1));

        Add(db, customerId, "HK-1007", OrderStatus.Pending, PaymentMethod.Card, 0, null,
            (Find("gulshan-haat-mart", "Farm eggs, dozen"), 1));

        await db.SaveChangesAsync();
    }

    private static void Add(
        ApplicationDbContext db,
        string customerId,
        string number,
        OrderStatus status,
        PaymentMethod method,
        int daysAgo,
        int? installmentCount,
        params (Product Product, int Quantity)[] lines)
    {
        var created = DateTime.UtcNow.AddDays(-daysAgo);
        var total = lines.Sum(l => l.Product.Price * l.Quantity);
        var parts = installmentCount is null ? null : InstallmentSchedule.Split(total, installmentCount.Value);
        var charge = parts is null ? total : parts[0];

        var order = new Order
        {
            OrderNumber = number,
            CustomerId = customerId,
            StoreId = lines[0].Product.StoreId,
            Status = status,
            Subtotal = total,
            Total = total,
            PaymentMethod = method,
            CreatedAt = created,
            UpdatedAt = status == OrderStatus.Pending ? null : created.AddHours(2),
            Payment = new Payment
            {
                Gateway = "Demo",
                Status = "Authorized",
                AuthorizationCode = "DEMO-SEED" + number[^4..],
                GatewayReference = "DEMO-SEED" + number[^4..],
                CardBrand = "Visa",
                CardLast4 = "4242",
                AmountAuthorized = charge,
                CreatedAt = created
            }
        };

        foreach (var (product, quantity) in lines)
        {
            product.Stock -= quantity;
            order.Lines.Add(new OrderLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                CategoryName = product.Category!.Name,
                CategoryId = product.CategoryId,
                UnitPrice = product.Price,
                Quantity = quantity,
                LineTotal = product.Price * quantity
            });
        }

        if (parts is not null)
        {
            var interval = InstallmentSchedule.IntervalDays(installmentCount!.Value);
            for (var i = 0; i < parts.Count; i++)
            {
                order.Installments.Add(new Installment
                {
                    Sequence = i + 1,
                    Amount = parts[i],
                    DueDate = created.Date.AddDays(interval * i),
                    Status = i == 0 ? InstallmentStatus.Authorized : InstallmentStatus.Scheduled,
                    PaidAt = i == 0 ? created : null,
                    AuthorizationCode = i == 0 ? order.Payment.AuthorizationCode : null
                });
            }
        }

        db.Orders.Add(order);
    }

    private sealed record StoreSeed(
        string Name, string Slug, string OwnerEmail, string Description, string Address, string Area,
        double Latitude, double Longitude, string Phone, string Hours, bool Approved, int DaysOld);

    private sealed record ProductSeed(
        string Store, string Category, string Name, string Description, decimal Price, int Stock, string Unit);

    private static readonly StoreSeed[] StoreSeeds =
    [
        new("Gulshan Haat Mart", "gulshan-haat-mart", "vendor.gulshan@haatekache.bd",
            "A corner mart for rice, oil, eggs, and a few things you forgot to buy.",
            "Road 45, Gulshan 2", "Gulshan", 23.7948, 90.4143, "01711-100001", "8:00–21:30", true, 40),
        new("Dhanmondi Paper Lane", "dhanmondi-paper-lane", "vendor.dhanmondi@haatekache.bd",
            "Copy paper, school kits, and desk supplies for the neighborhood.",
            "Road 27, Dhanmondi", "Dhanmondi", 23.7556, 90.3760, "01711-100002", "9:00–20:30", true, 36),
        new("Mirpur Rupa Jewelers", "mirpur-rupa-jewelers", "vendor.mirpur@haatekache.bd",
            "Everyday jewelry, set and polished in Mirpur 10.",
            "Mirpur 10 circle", "Mirpur", 23.8069, 90.3686, "01711-100003", "11:00–21:00", true, 30),
        new("Uttara Loom & Cloth", "uttara-loom-cloth", "vendor.uttara@haatekache.bd",
            "Punjabi, saree, and ready shirts you can try on before pickup.",
            "Sector 7, Uttara", "Uttara", 23.8689, 90.3900, "01711-100004", "10:00–21:00", true, 28),
        new("Banani Daily Basket", "banani-daily-basket", "vendor.banani@haatekache.bd",
            "Groceries and a small mix of household goods on Road 11.",
            "Road 11, Banani", "Banani", 23.7937, 90.4066, "01711-100005", "8:00–22:00", true, 22),
        new("Mohammadpur Para Store", "mohammadpur-para-store", "vendor.mohammadpur@haatekache.bd",
            "A para shop with staples, cloth, and school things under one shutter.",
            "Tajmahal Road, Mohammadpur", "Mohammadpur", 23.7658, 90.3584, "01711-100006", "8:30–21:00", true, 18),
        new("Lalmatia Spice Cupboard", "lalmatia-spice-cupboard", "vendor.pending@haatekache.bd",
            "A new pantry waiting for the owner to approve the stall.",
            "Lalmatia Block B", "Lalmatia", 23.7564, 90.3675, "01711-100007", "9:00–20:00", false, 1)
    ];

    private static readonly ProductSeed[] ProductSeeds =
    [
        new("gulshan-haat-mart", "groceries", "Miniket rice, 5 kg", "Aged miniket for everyday meals, packed in a 5 kg bag.", 680m, 40, "bag"),
        new("gulshan-haat-mart", "groceries", "Farm eggs, dozen", "A dozen brown eggs from a Gazipur farm, dated this week.", 165m, 60, "dozen"),
        new("gulshan-haat-mart", "groceries", "Mustard oil, 1 L", "Cold-pressed mustard oil in a sealed litre bottle.", 295m, 35, "bottle"),
        new("gulshan-haat-mart", "groceries", "Red lentils, 1 kg", "Washed masoor dal, 1 kg pouch.", 155m, 50, "kg"),
        new("gulshan-haat-mart", "groceries", "Loose black tea, 200 g", "Strong black tea scooped and sealed at the counter.", 120m, 40, "pack"),
        new("gulshan-haat-mart", "stationery", "Ruled exercise notebook", "80-page ruled notebook for school or the shop ledger.", 55m, 80, "pc"),
        new("gulshan-haat-mart", "clothing", "Handwoven cotton scarf", "Light cotton scarf, one size, woven in Narayanganj.", 540m, 18, "pc"),
        new("gulshan-haat-mart", "jewelry", "Seed bead bracelet", "Adjustable bracelet of small glass beads.", 420m, 22, "pc"),

        new("dhanmondi-paper-lane", "stationery", "A4 copy paper, 500 sheets", "70 gsm A4 paper, one ream, for home printers.", 540m, 30, "ream"),
        new("dhanmondi-paper-lane", "stationery", "Ballpoint pens, pack of 10", "Blue ballpoints that write on exam paper.", 90m, 70, "pack"),
        new("dhanmondi-paper-lane", "stationery", "Student geometry box", "Compass, divider, and two set squares in a tin.", 175m, 25, "box"),
        new("dhanmondi-paper-lane", "stationery", "Desktop stapler", "Metal stapler with a small box of pins.", 240m, 20, "pc"),
        new("dhanmondi-paper-lane", "stationery", "Sticky note pad", "Three pastel pads, 100 sheets each.", 75m, 40, "pack"),
        new("dhanmondi-paper-lane", "stationery", "Highlighter set", "Four fluorescent highlighters.", 160m, 28, "set"),
        new("dhanmondi-paper-lane", "groceries", "Wholewheat atta, 2 kg", "Chakki atta for daily ruti, 2 kg pack.", 140m, 36, "pack"),
        new("dhanmondi-paper-lane", "clothing", "Pique polo shirt", "Cotton polo in navy, medium and large.", 1150m, 14, "pc"),

        new("mirpur-rupa-jewelers", "jewelry", "Gold-plated hoop earrings", "Medium hoops with a gold-tone plate and butterfly backs.", 1450m, 12, "pair"),
        new("mirpur-rupa-jewelers", "jewelry", "Silver nose pin", "Small sterling nose pin with a clear stone.", 680m, 16, "pc"),
        new("mirpur-rupa-jewelers", "jewelry", "Kundan necklace", "Statement necklace set with kundan-style stones.", 3400m, 6, "pc"),
        new("mirpur-rupa-jewelers", "jewelry", "Pearl stud earrings", "Freshwater pearl studs on silver posts.", 920m, 14, "pair"),
        new("mirpur-rupa-jewelers", "jewelry", "Men's curb chain", "Gold-tone curb chain, 20 inch.", 2150m, 8, "pc"),
        new("mirpur-rupa-jewelers", "jewelry", "Glass bangle set", "Set of six glass bangles in bridal red.", 980m, 15, "set"),
        new("mirpur-rupa-jewelers", "clothing", "Printed kids tee", "Cotton tee for ages 6–8, printed with a river boat.", 480m, 20, "pc"),
        new("mirpur-rupa-jewelers", "groceries", "Green chili, 250 g", "Fresh green chili, weighed at 250 g.", 45m, 40, "pack"),

        new("uttara-loom-cloth", "clothing", "Cotton punjabi", "White cotton punjabi with a narrow collar, sizes M–XL.", 1890m, 12, "pc"),
        new("uttara-loom-cloth", "clothing", "Linen casual shirt", "Breathable linen shirt in sand.", 1720m, 10, "pc"),
        new("uttara-loom-cloth", "clothing", "Lawn three-piece", "Printed lawn kameez, salwar, and dupatta.", 2650m, 8, "set"),
        new("uttara-loom-cloth", "clothing", "Indigo denim jeans", "Straight denim, mid rise, sizes 30–34.", 2190m, 11, "pc"),
        new("uttara-loom-cloth", "clothing", "Handloom saree", "Tangail-style cotton saree with a contrasting border.", 3450m, 7, "pc"),
        new("uttara-loom-cloth", "stationery", "Colored pencil set", "Twelve colored pencils in a tin.", 210m, 24, "set"),
        new("uttara-loom-cloth", "jewelry", "Silver anklet pair", "Light silver anklets with tiny bells.", 760m, 10, "pair"),
        new("uttara-loom-cloth", "groceries", "Fresh milk, 1 L", "Pasteurized milk, keep chilled and pick up today.", 100m, 30, "L"),

        new("banani-daily-basket", "groceries", "Brown eggs, dozen", "A dozen brown eggs, packed in a pulp tray.", 170m, 48, "dozen"),
        new("banani-daily-basket", "groceries", "Chakki atta, 2 kg", "Stone-ground atta, 2 kg.", 145m, 32, "pack"),
        new("banani-daily-basket", "groceries", "Family tea, 400 g", "Leaf tea for a full week of morning cups.", 210m, 26, "pack"),
        new("banani-daily-basket", "groceries", "Miniket rice, 1 kg", "A single kilo of miniket when you only need tonight's pot.", 145m, 40, "kg"),
        new("banani-daily-basket", "jewelry", "Small pearl studs", "Petite pearl studs for everyday wear.", 750m, 12, "pair"),
        new("banani-daily-basket", "stationery", "Desk highlighters", "Two chisel-tip highlighters.", 140m, 30, "set"),
        new("banani-daily-basket", "clothing", "Striped kids tee", "Navy and white stripes, ages 8–10.", 520m, 16, "pc"),
        new("banani-daily-basket", "stationery", "Softcover notebook", "A5 softcover, 120 ruled pages.", 60m, 50, "pc"),

        new("mohammadpur-para-store", "groceries", "Mustard oil, 500 ml", "Half-litre mustard oil for a smaller kitchen.", 160m, 28, "bottle"),
        new("mohammadpur-para-store", "groceries", "Red lentils, 500 g", "Masoor dal in a 500 g pack.", 85m, 40, "pack"),
        new("mohammadpur-para-store", "groceries", "Layer eggs, dozen", "Layer eggs, one dozen.", 150m, 36, "dozen"),
        new("mohammadpur-para-store", "stationery", "School geometry box", "A compact geometry box for class six and up.", 160m, 22, "box"),
        new("mohammadpur-para-store", "stationery", "Pastel sticky notes", "A cube of pastel notes.", 65m, 34, "pc"),
        new("mohammadpur-para-store", "clothing", "Tangail cotton saree", "Soft cotton saree with a temple border.", 2980m, 6, "pc"),
        new("mohammadpur-para-store", "clothing", "Everyday scarf", "A plain cotton scarf in rust.", 490m, 14, "pc"),
        new("mohammadpur-para-store", "jewelry", "Beaded anklet", "Single-strand bead anklet.", 640m, 11, "pc")
    ];
}
