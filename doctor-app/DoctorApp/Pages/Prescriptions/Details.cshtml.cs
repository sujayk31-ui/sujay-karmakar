using DoctorApp.Data;
using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Prescriptions;

public class DetailsModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public DetailsModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public Prescription Rx { get; private set; } = new();
    public string Text { get; private set; } = "";
    public string WhatsApp { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        var rx = await _db.Prescriptions.AsNoTracking()
            .Include(p => p.Items)
            .Include(p => p.Tests)
            .FirstOrDefaultAsync(p => p.Id == id && p.DoctorId == doctor.Id);
        if (rx == null)
            return NotFound();
        Rx = rx;
        Text = PrescriptionText.Build(rx);
        WhatsApp = PrescriptionText.WhatsAppUrl(rx);
        return Page();
    }
}
