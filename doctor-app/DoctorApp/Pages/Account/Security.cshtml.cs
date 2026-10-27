using System.ComponentModel.DataAnnotations;
using DoctorApp.Data;
using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Account;

public class SecurityModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;

    public SecurityModel(UserManager<ApplicationUser> users, AppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public ApplicationUser Doctor { get; private set; } = new();
    public List<WebAuthnCredential> Credentials { get; private set; } = new();

    [BindProperty]
    public ProfileInput Profile { get; set; } = new();

    public string? Saved { get; private set; }

    public class ProfileInput
    {
        [Required, Display(Name = "Full name")]
        public string FullName { get; set; } = "";

        [Required]
        public string Degree { get; set; } = "";

        [Required]
        public string Phone { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        Profile = new ProfileInput
        {
            FullName = doctor.FullName,
            Degree = doctor.Degree,
            Phone = doctor.PhoneNumber ?? ""
        };
        await LoadCredentialsAsync(doctor);
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        Profile.FullName = Profile.FullName.Trim();
        Profile.Degree = Profile.Degree.Trim();
        Profile.Phone = Profile.Phone.Trim();
        if (PhoneNumbers.Digits(Profile.Phone).Length < 10)
            ModelState.AddModelError("Profile.Phone", "Enter a phone number with at least 10 digits.");
        if (!ModelState.IsValid)
        {
            await LoadCredentialsAsync(doctor);
            return Page();
        }

        doctor.FullName = Profile.FullName;
        doctor.Degree = Profile.Degree;
        doctor.PhoneNumber = Profile.Phone;
        await _users.UpdateAsync(doctor);
        await LoadCredentialsAsync(doctor);
        Saved = "Prescription header saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        var credential = await _db.WebAuthnCredentials.FirstOrDefaultAsync(c => c.Id == id && c.UserId == doctor.Id);
        if (credential != null)
        {
            _db.WebAuthnCredentials.Remove(credential);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    private async Task LoadCredentialsAsync(ApplicationUser doctor)
    {
        Doctor = doctor;
        Credentials = await _db.WebAuthnCredentials.AsNoTracking()
            .Where(c => c.UserId == doctor.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }
}
