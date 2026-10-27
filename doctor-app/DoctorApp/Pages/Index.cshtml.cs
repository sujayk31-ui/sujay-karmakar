using DoctorApp.Data;
using DoctorApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages;

public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public IndexModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public string DoctorName { get; private set; } = "";
    public bool HasPasskey { get; private set; }
    public List<Prescription> Recent { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        DoctorName = doctor?.FullName ?? User.Identity?.Name ?? "";
        if (doctor == null)
            return;
        HasPasskey = await _db.WebAuthnCredentials.AnyAsync(c => c.UserId == doctor.Id);
        Recent = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.DoctorId == doctor.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .ToListAsync();
    }
}
