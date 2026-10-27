using DoctorApp.Data;
using DoctorApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Medicines;

public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public IndexModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public List<CatalogProduct> Items { get; private set; } = new();

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Composition { get; set; } = "";
    [BindProperty] public string? CompanyName { get; set; }
    [BindProperty] public string? DoseForm { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        await LoadAsync(doctor.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        Name = Name.Trim();
        Composition = Composition.Trim();
        CompanyName = string.IsNullOrWhiteSpace(CompanyName) ? null : CompanyName.Trim();
        DoseForm = string.IsNullOrWhiteSpace(DoseForm) ? null : DoseForm.Trim();
        if (Name.Length < 3)
            ModelState.AddModelError(nameof(Name), "Enter the medicine name.");
        if (Composition.Length < 3)
            ModelState.AddModelError(nameof(Composition), "Enter the composition.");
        if (!ModelState.IsValid)
        {
            await LoadAsync(doctor.Id);
            return Page();
        }

        _db.CatalogProducts.Add(new CatalogProduct
        {
            SourceKey = $"custom:{doctor.Id}:{Guid.NewGuid():N}",
            Name = Name,
            BrandName = Name,
            CompanyName = CompanyName,
            DoseForm = DoseForm,
            Composition = Composition,
            Kind = "custom",
            IsCustom = true,
            OwnerDoctorId = doctor.Id,
            SourceName = "Added by this doctor",
            SourceDetail = "Custom medicine entered in Vaidya. Not taken from a company catalog.",
            RetrievedOn = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadAsync(string doctorId)
    {
        Items = await _db.CatalogProducts.AsNoTracking()
            .Where(p => p.IsCustom && p.OwnerDoctorId == doctorId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}
