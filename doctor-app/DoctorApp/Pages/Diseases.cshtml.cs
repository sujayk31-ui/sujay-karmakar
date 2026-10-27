using DoctorApp.Data;
using DoctorApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages;

public class DiseasesModel : PageModel
{
    private readonly AppDbContext _db;
    public DiseasesModel(AppDbContext db) => _db = db;

    public List<DiseaseCategory> Categories { get; private set; } = new();
    public int SubcategoryCount { get; private set; }

    public async Task OnGetAsync()
    {
        Categories = await _db.DiseaseCategories.AsNoTracking()
            .Include(c => c.Subcategories)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
        foreach (var category in Categories)
            category.Subcategories = category.Subcategories.OrderBy(s => s.SortOrder).ToList();
        SubcategoryCount = Categories.Sum(c => c.Subcategories.Count);
    }
}
