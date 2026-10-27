using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Account;

public class LoginModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IPasswordHasher<ApplicationUser> _pins;

    public LoginModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IPasswordHasher<ApplicationUser> pins)
    {
        _users = users;
        _signIn = signIn;
        _pins = pins;
    }

    [BindProperty]
    public string? UserId { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? Pin { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Message { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var name = (UserId ?? "").Trim();
        var password = Password ?? "";
        var doctor = await _users.FindByNameAsync(name);
        if (doctor == null)
        {
            Message = "Those details did not match a doctor account.";
            return Page();
        }

        var result = await _signIn.PasswordSignInAsync(doctor, password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
            return LocalRedirect(SafeReturn());
        Message = result.IsLockedOut
            ? "This account is locked for a few minutes."
            : "Those details did not match a doctor account.";
        return Page();
    }

    public async Task<IActionResult> OnPostPinAsync()
    {
        var pin = (Pin ?? "").Trim();
        if (!PinRules.SixDigits().IsMatch(pin))
        {
            Message = "Enter the 6-digit PIN.";
            return Page();
        }

        ApplicationUser? match = null;
        foreach (var doctor in await _users.Users.ToListAsync())
        {
            if (string.IsNullOrEmpty(doctor.PinHash))
                continue;
            var verified = _pins.VerifyHashedPassword(doctor, doctor.PinHash, pin);
            if (verified == PasswordVerificationResult.Failed)
                continue;
            if (verified == PasswordVerificationResult.SuccessRehashNeeded)
            {
                doctor.PinHash = _pins.HashPassword(doctor, pin);
                await _users.UpdateAsync(doctor);
            }
            match = doctor;
            break;
        }

        if (match == null)
        {
            Message = "Those details did not match a doctor account.";
            return Page();
        }
        if (await _users.IsLockedOutAsync(match))
        {
            Message = "This account is locked for a few minutes.";
            return Page();
        }

        await _signIn.SignInAsync(match, isPersistent: true);
        return LocalRedirect(SafeReturn());
    }

    private string SafeReturn() =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
