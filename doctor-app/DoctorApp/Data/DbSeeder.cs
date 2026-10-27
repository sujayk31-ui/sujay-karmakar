using System.Text.Json;
using DoctorApp.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IWebHostEnvironment env)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.CatalogProducts.AnyAsync())
            return;

        var path = Path.Combine(env.ContentRootPath, "Data", "Seed", "catalog.json");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "catalog.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("Medicine seed file was not found at Data/Seed/catalog.json.");

        var file = await JsonSerializer.DeserializeAsync<SeedFile>(
            File.OpenRead(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Medicine seed file was empty.");

        foreach (var category in file.Categories.OrderBy(c => c.Sort))
        {
            var entity = new DiseaseCategory
            {
                Slug = category.Slug,
                Name = category.Name,
                Icd11 = category.Icd11 ?? "",
                Citation = category.Citation ?? "",
                SortOrder = category.Sort
            };
            foreach (var sub in category.Subcategories.OrderBy(s => s.Sort))
            {
                entity.Subcategories.Add(new DiseaseSubcategory
                {
                    Slug = sub.Slug,
                    Name = sub.Name,
                    Aliases = sub.Aliases ?? "",
                    Icd11 = sub.Icd11,
                    SortOrder = sub.Sort
                });
            }
            db.DiseaseCategories.Add(entity);
        }

        foreach (var test in file.Tests.OrderBy(t => t.Sort))
        {
            db.DiagnosticTests.Add(new DiagnosticTest
            {
                Category = test.Category,
                Name = test.Name,
                SortOrder = test.Sort
            });
        }
        await db.SaveChangesAsync();
        var subIds = await db.DiseaseSubcategories.AsNoTracking()
            .ToDictionaryAsync(s => s.Slug, s => s.Id, StringComparer.OrdinalIgnoreCase);

        var batch = new List<CatalogProduct>(400);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in file.Products)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Length < 3)
                continue;
            if (!seenKeys.Add(row.Key))
                continue;
            if (row.Name.Equals("and", StringComparison.OrdinalIgnoreCase))
                continue;

            var product = new CatalogProduct
            {
                SourceKey = row.Key,
                Name = row.Name.Trim(),
                BrandName = row.Brand,
                CompanyName = string.IsNullOrWhiteSpace(row.Company) ? null : row.Company.Trim(),
                DoseForm = row.Form,
                Composition = string.IsNullOrWhiteSpace(row.Composition) ? row.Name.Trim() : row.Composition.Trim(),
                Pack = row.Pack,
                MrpInr = row.Mrp,
                PriceAsOf = row.PriceAsOf,
                PriceSource = row.PriceSource,
                CareLevels = row.Care,
                SectionCode = row.SectionCode,
                SectionTitle = row.SectionTitle,
                SourceName = row.SourceName ?? "",
                SourceUrl = row.SourceUrl ?? "",
                SourceDetail = row.SourceDetail ?? "",
                RetrievedOn = row.Retrieved ?? "",
                Kind = row.Kind ?? "",
                IsCustom = false
            };
            foreach (var tag in row.Tags ?? new List<SeedTag>())
            {
                int? subId = null;
                if (!string.IsNullOrWhiteSpace(tag.Sub) && subIds.TryGetValue(tag.Sub, out var id))
                    subId = id;
                product.Tags.Add(new ProductTag
                {
                    Label = tag.Label ?? "",
                    Origin = string.IsNullOrWhiteSpace(tag.Origin) ? "inferred" : tag.Origin,
                    SubcategoryId = subId,
                    Note = tag.Note
                });
            }
            batch.Add(product);
            if (batch.Count >= 400)
            {
                db.CatalogProducts.AddRange(batch);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            db.CatalogProducts.AddRange(batch);
            await db.SaveChangesAsync();
        }
    }

    private sealed class SeedFile
    {
        public List<SeedCategory> Categories { get; set; } = new();
        public List<SeedTest> Tests { get; set; } = new();
        public List<SeedProduct> Products { get; set; } = new();
    }

    private sealed class SeedCategory
    {
        public string Slug { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Icd11 { get; set; }
        public string? Citation { get; set; }
        public int Sort { get; set; }
        public List<SeedSub> Subcategories { get; set; } = new();
    }

    private sealed class SeedSub
    {
        public string Slug { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Aliases { get; set; }
        public string? Icd11 { get; set; }
        public int Sort { get; set; }
    }

    private sealed class SeedTest
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public int Sort { get; set; }
    }

    private sealed class SeedProduct
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Brand { get; set; }
        public string? Company { get; set; }
        public string? Form { get; set; }
        public string? Composition { get; set; }
        public string? Pack { get; set; }
        public decimal? Mrp { get; set; }
        public string? PriceAsOf { get; set; }
        public string? PriceSource { get; set; }
        public string? Care { get; set; }
        public string? SectionCode { get; set; }
        public string? SectionTitle { get; set; }
        public string? SourceName { get; set; }
        public string? SourceUrl { get; set; }
        public string? SourceDetail { get; set; }
        public string? Retrieved { get; set; }
        public string? Kind { get; set; }
        public List<SeedTag>? Tags { get; set; }
    }

    private sealed class SeedTag
    {
        public string? Label { get; set; }
        public string? Origin { get; set; }
        public string? Sub { get; set; }
        public string? Note { get; set; }
    }
}
