using DoctorApp.Data;
using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Patients;

public class DetailsModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public DetailsModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public Patient Person { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        var patient = await _db.Patients.AsNoTracking()
            .Include(p => p.Prescriptions).ThenInclude(p => p.Items)
            .Include(p => p.Prescriptions).ThenInclude(p => p.Tests)
            .FirstOrDefaultAsync(p => p.Id == id && p.DoctorId == doctor.Id);
        if (patient == null)
            return NotFound();
        patient.Prescriptions = patient.Prescriptions.OrderByDescending(p => p.CreatedAt).ToList();
        Person = patient;
        return Page();
    }
}
