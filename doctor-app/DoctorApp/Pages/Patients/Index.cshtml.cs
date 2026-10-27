using DoctorApp.Data;
using DoctorApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Patients;

public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public IndexModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public List<Patient> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return;
        Items = await _db.Patients.AsNoTracking()
            .Include(p => p.Prescriptions)
            .Where(p => p.DoctorId == doctor.Id)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}
