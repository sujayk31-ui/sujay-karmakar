using DoctorApp.Data;
using DoctorApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Medicines;

public class EditModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public EditModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Composition { get; set; } = "";
    [BindProperty] public string? CompanyName { get; set; }
    [BindProperty] public string? DoseForm { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var product = await FindAsync();
        if (product == null)
            return NotFound();
        Name = product.Name;
        Composition = product.Composition;
        CompanyName = product.CompanyName;
        DoseForm = product.DoseForm;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var product = await FindAsync();
        if (product == null)
            return NotFound();
        Name = Name.Trim();
        Composition = Composition.Trim();
        if (Name.Length < 3)
            ModelState.AddModelError(nameof(Name), "Enter the medicine name.");
        if (Composition.Length < 3)
            ModelState.AddModelError(nameof(Composition), "Enter the composition.");
        if (!ModelState.IsValid)
            return Page();
        product.Name = Name;
        product.BrandName = Name;
        product.Composition = Composition;
        product.CompanyName = string.IsNullOrWhiteSpace(CompanyName) ? null : CompanyName.Trim();
        product.DoseForm = string.IsNullOrWhiteSpace(DoseForm) ? null : DoseForm.Trim();
        await _db.SaveChangesAsync();
        return RedirectToPage("/Medicines/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var product = await FindAsync();
        if (product != null)
        {
            _db.CatalogProducts.Remove(product);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage("/Medicines/Index");
    }

    private async Task<CatalogProduct?> FindAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return null;
        return await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == Id && p.IsCustom && p.OwnerDoctorId == doctor.Id);
    }
}
