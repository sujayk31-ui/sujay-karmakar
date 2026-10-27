using System.Text;
using System.Text.RegularExpressions;
using DoctorApp.Data;
using DoctorApp.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Services;

public static class PhoneNumbers
{
    public static string Digits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "";
        return new string(phone.Where(char.IsDigit).ToArray());
    }

    public static string WhatsAppNumber(string? phone)
    {
        var digits = Digits(phone);
        if (digits.Length == 10)
            return "91" + digits;
        if (digits.Length == 11 && digits.StartsWith('0'))
            return "91" + digits[1..];
        if (digits.Length == 12 && digits.StartsWith("91"))
            return digits;
        return "";
    }

    public static string Display(string? phone)
    {
        var wa = WhatsAppNumber(phone);
        if (wa.Length == 12)
            return $"+91 {wa[2..7]} {wa[7..]}";
        return phone?.Trim() ?? "";
    }
}

public static class PrescriptionText
{
    public static string Build(Prescription rx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Vaidya prescription");
        sb.AppendLine($"Dr. {rx.DoctorName}");
        if (!string.IsNullOrWhiteSpace(rx.DoctorDegree))
            sb.AppendLine(rx.DoctorDegree);
        sb.AppendLine($"Ph: {PhoneNumbers.Display(rx.DoctorPhone)}");
        sb.AppendLine();
        sb.AppendLine($"Patient: {rx.PatientName}");
        sb.AppendLine($"Age: {rx.PatientAge}");
        sb.AppendLine($"Weight: {rx.PatientWeightKg:0.#} kg");
        sb.AppendLine($"BP: {rx.BloodPressure}");
        sb.AppendLine($"Date: {rx.CreatedAt.ToLocalTime():dd MMM yyyy}");
        if (!string.IsNullOrWhiteSpace(rx.Diagnosis))
            sb.AppendLine($"Diagnosis: {rx.Diagnosis}");
        sb.AppendLine();
        sb.AppendLine("Medicines");
        var n = 1;
        foreach (var item in rx.Items)
        {
            sb.AppendLine($"{n}. {item.MedicineName}");
            var composition = item.Composition;
            if (!string.IsNullOrWhiteSpace(item.CompanyName))
                composition = string.IsNullOrWhiteSpace(composition)
                    ? item.CompanyName
                    : $"{composition} — {item.CompanyName}";
            if (!string.IsNullOrWhiteSpace(composition))
                sb.AppendLine($"   {composition}");
            var use = string.Join(" · ", new[] { item.Dosage, item.Duration, item.Instructions }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (use.Length > 0)
                sb.AppendLine($"   {use}");
            n++;
        }
        if (rx.Tests.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Tests");
            foreach (var test in rx.Tests)
                sb.AppendLine(test.IsCustom ? $"- {test.Name} (added by doctor)" : $"- {test.Name}");
        }
        if (!string.IsNullOrWhiteSpace(rx.Notes))
        {
            sb.AppendLine();
            sb.AppendLine(rx.Notes.Trim());
        }
        return sb.ToString().Trim();
    }

    public static string WhatsAppUrl(Prescription rx)
    {
        var text = Uri.EscapeDataString(Build(rx));
        var phone = PhoneNumbers.WhatsAppNumber(rx.PatientPhone);
        return string.IsNullOrEmpty(phone)
            ? $"https://wa.me/?text={text}"
            : $"https://wa.me/{phone}?text={text}";
    }
}

public sealed class SearchRequest
{
    public string? Q { get; set; }
    public string? Sub { get; set; }
    public string? Category { get; set; }
    public string? Company { get; set; }
    public bool Antibiotic { get; set; }
}

public sealed class CompanyGroup
{
    public string Name { get; set; } = "";
    public string KindLabel { get; set; } = "";
    public int Total { get; set; }
    public List<CatalogProduct> Products { get; set; } = new();
}

public sealed class SearchView
{
    public string Mode { get; set; } = "text";
    public string Title { get; set; } = "Search";
    public string? Note { get; set; }
    public string Query { get; set; } = "";
    public bool Antibiotic { get; set; }
    public List<CompanyGroup> Companies { get; set; } = new();
    public List<CatalogProduct> Products { get; set; } = new();
    public int Total { get; set; }
}

public class ClinicSearch
{
    private readonly AppDbContext _db;
    public ClinicSearch(AppDbContext db) => _db = db;

    public async Task<SearchView> RunAsync(SearchRequest request, string? doctorId)
    {
        var q = (request.Q ?? "").Trim();
        var view = new SearchView { Query = q, Antibiotic = request.Antibiotic };
        var products = Visible(doctorId);

        if (!string.IsNullOrWhiteSpace(request.Company))
        {
            var name = request.Company.Trim();
            var companyProducts = products.Where(p => p.CompanyName != null && p.CompanyName.ToLower() == name.ToLower());
            if (!string.IsNullOrWhiteSpace(q))
            {
                var nq = q.ToLower();
                companyProducts = companyProducts.Where(p =>
                    p.Name.ToLower().Contains(nq) || p.Composition.ToLower().Contains(nq) || (p.BrandName != null && p.BrandName.ToLower().Contains(nq)));
            }
            if (request.Antibiotic)
                companyProducts = companyProducts.Where(p => p.Tags.Any(t => t.Label == "antibiotic" || t.Label == "antibacterial"));
            return await CompanyView(view, companyProducts, $"Medicines from {name}", exact: true);
        }

        if (!string.IsNullOrWhiteSpace(request.Sub))
        {
            var sub = await _db.DiseaseSubcategories.Include(s => s.Category)
                .FirstOrDefaultAsync(s => s.Slug == request.Sub);
            if (sub != null)
            {
                var set = products.Where(p => p.Tags.Any(t => t.SubcategoryId == sub.Id));
                if (request.Antibiotic)
                    set = set.Where(p => p.Tags.Any(t => t.Label == "antibiotic" || t.Label == "antibacterial"));
                var list = await set.OrderBy(p => p.Name).Take(120).ToListAsync();
                view.Mode = "disease";
                view.Title = sub.Name;
                view.Note = $"In {sub.Category?.Name}. Tags on these rows are inferred unless marked section. Not a product-licence indication.";
                view.Products = list;
                view.Total = await set.CountAsync();
                return view;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = await _db.DiseaseCategories.Include(c => c.Subcategories)
                .FirstOrDefaultAsync(c => c.Slug == request.Category);
            if (category != null)
            {
                var ids = category.Subcategories.Select(s => s.Id).ToList();
                var set = products.Where(p => p.Tags.Any(t => t.SubcategoryId != null && ids.Contains(t.SubcategoryId.Value)));
                if (request.Antibiotic)
                    set = set.Where(p => p.Tags.Any(t => t.Label == "antibiotic" || t.Label == "antibacterial"));
                view.Mode = "category";
                view.Title = category.Name;
                view.Note = category.Citation;
                view.Products = await set.OrderBy(p => p.Name).Take(120).ToListAsync();
                view.Total = await set.CountAsync();
                return view;
            }
        }

        if (q.Length == 0)
        {
            view.Mode = "empty";
            view.Title = "Search medicines";
            return view;
        }

        var normalized = q.ToLowerInvariant();
        var subHit = await MatchSubcategory(normalized);
        if (subHit != null && !request.Antibiotic)
        {
            request.Sub = subHit.Slug;
            request.Q = null;
            return await RunAsync(request, doctorId);
        }
        if (subHit != null && request.Antibiotic)
        {
            request.Sub = subHit.Slug;
            return await RunAsync(request, doctorId);
        }

        if (normalized is "antibiotic" or "antibiotics")
        {
            request.Antibiotic = true;
            request.Q = null;
            var set = products.Where(p => p.Tags.Any(t => t.Label == "antibiotic" || t.Label == "antibacterial"));
            view.Mode = "disease";
            view.Title = "Antibiotics";
            view.Antibiotic = true;
            view.Note = "Antibacterial class tag. NLEM section 6.2 is the section source. Antimalarials and antifungals are not included unless they also carry this tag. Inferred tags are marked.";
            view.Products = await set.OrderBy(p => p.Name).Take(120).ToListAsync();
            view.Total = await set.CountAsync();
            return view;
        }

        var categoryHit = await _db.DiseaseCategories.FirstOrDefaultAsync(c => c.Name.ToLower() == normalized || c.Slug == normalized);
        if (categoryHit != null)
        {
            request.Category = categoryHit.Slug;
            request.Q = null;
            return await RunAsync(request, doctorId);
        }

        var companyNames = await MatchingCompanies(products, normalized);
        if (companyNames.Count > 0)
        {
            var set = products.Where(p => p.CompanyName != null && companyNames.Contains(p.CompanyName));
            if (request.Antibiotic)
                set = set.Where(p => p.Tags.Any(t => t.Label == "antibiotic" || t.Label == "antibacterial"));
            return await CompanyView(view, set, companyNames.Count == 1 ? $"Medicines from {companyNames[0]}" : "Companies", exact: false);
        }

        var text = products.Where(p =>
            p.Name.ToLower().Contains(normalized) ||
            p.Composition.ToLower().Contains(normalized) ||
            (p.CompanyName != null && p.CompanyName.ToLower().Contains(normalized)) ||
            (p.BrandName != null && p.BrandName.ToLower().Contains(normalized)) ||
            p.Tags.Any(t => t.Label.ToLower().Contains(normalized)));
        if (request.Antibiotic)
            text = text.Where(p => p.Tags.Any(t => t.Label == "antibiotic" || t.Label == "antibacterial"));
        view.Mode = "text";
        view.Title = $"Results for “{q}”";
        view.Products = await text.OrderBy(p => p.Name).Take(80).ToListAsync();
        view.Total = await text.CountAsync();
        view.Note = "Name, composition, company, and tags. Disease tags are inferred unless marked section.";
        return view;
    }

    public async Task<List<CompanyGroup>> ListCompaniesAsync(string? doctorId)
    {
        var rows = await Visible(doctorId)
            .Where(p => p.CompanyName != null && p.CompanyName != "")
            .GroupBy(p => p.CompanyName!)
            .Select(g => new { Name = g.Key, Count = g.Count(), Kind = g.Min(p => p.Kind) })
            .OrderBy(g => g.Name)
            .ToListAsync();
        return rows.Select(r => new CompanyGroup
        {
            Name = r.Name,
            Total = r.Count,
            KindLabel = KindLabel(r.Kind ?? "", r.Count)
        }).ToList();
    }

    public IQueryable<CatalogProduct> Visible(string? doctorId) =>
        _db.CatalogProducts.AsNoTracking()
            .Include(p => p.Tags)
            .Where(p => !p.IsCustom || p.OwnerDoctorId == doctorId);

    private async Task<DiseaseSubcategory?> MatchSubcategory(string normalized)
    {
        var subs = await _db.DiseaseSubcategories.AsNoTracking().ToListAsync();
        return subs.FirstOrDefault(s =>
            s.Name.ToLower() == normalized ||
            s.Slug == normalized ||
            s.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(a => a.ToLower() == normalized));
    }

    private static async Task<List<string>> MatchingCompanies(IQueryable<CatalogProduct> products, string normalized)
    {
        var names = await products
            .Where(p => p.CompanyName != null && p.CompanyName != "")
            .Select(p => p.CompanyName!)
            .Distinct()
            .ToListAsync();
        var hits = new List<string>();
        foreach (var name in names)
        {
            if (CompanyAliases(name).Any(a => a == normalized))
                hits.Add(name);
            else if (normalized.Length >= 4 && name.ToLowerInvariant().Contains(normalized))
                hits.Add(name);
        }
        return hits;
    }

    public static IEnumerable<string> CompanyAliases(string name)
    {
        var lower = name.ToLowerInvariant();
        yield return lower;
        if (lower.Contains("mankind"))
            yield return "mankind";
        if (lower.Contains("sun pharmaceutical") || lower.StartsWith("sun "))
        {
            yield return "sun";
            yield return "sun pharma";
            yield return "sun pharmaceutical";
        }
        if (lower.Contains("janaushadhi") || lower.Contains("pmbjp") || lower.Contains("jan aushadhi"))
        {
            yield return "janaushadhi";
            yield return "jan aushadhi";
            yield return "pmbjp";
            yield return "pmbj";
        }
    }

    private static async Task<SearchView> CompanyView(SearchView view, IQueryable<CatalogProduct> set, string title, bool exact)
    {
        var all = await set.OrderBy(p => p.BrandName).ThenBy(p => p.Name).ToListAsync();
        view.Mode = "company";
        view.Title = title;
        view.Total = all.Count;
        view.Companies = all.GroupBy(p => p.CompanyName ?? "Unknown company")
            .Select(g =>
            {
                var list = g.ToList();
                return new CompanyGroup
                {
                    Name = g.Key,
                    Total = list.Count,
                    KindLabel = KindLabel(list[0].Kind, list.Count),
                    Products = list
                };
            })
            .OrderBy(g => g.Name)
            .ToList();
        var priced = all.Count(p => p.MrpInr != null);
        view.Note = $"Every loaded pack for this company, with composition. {priced} of {all.Count} have an MRP the source actually printed. An empty price means that source had none. NLEM rows do not name a marketer. This is not the full Indian market.";
        return view;
    }

    private static string KindLabel(string kind, int count)
    {
        return kind switch
        {
            "brand-sample" => "Cited brand sample, not the full catalog",
            "company" => "Public company table. Price only when the source printed an MRP",
            "janaushadhi" => "PMBJP generic list, the scheme rather than a commercial brand",
            "custom" => "Added by a doctor in this clinic",
            _ => count == 1 ? "One loaded medicine" : $"{count} loaded medicines"
        };
    }
}

public static partial class PinRules
{
    [GeneratedRegex(@"^\d{6}$")]
    public static partial Regex SixDigits();

    [GeneratedRegex(@"^\d{2,3}\s*/\s*\d{2,3}(\s*mmHg)?$", RegexOptions.IgnoreCase)]
    public static partial Regex BloodPressure();
}
