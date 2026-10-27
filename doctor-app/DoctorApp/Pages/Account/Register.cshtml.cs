using System.ComponentModel.DataAnnotations;
using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IPasswordHasher<ApplicationUser> _pins;

    public RegisterModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IPasswordHasher<ApplicationUser> pins)
    {
        _users = users;
        _signIn = signIn;
        _pins = pins;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, Display(Name = "Full name")]
        public string FullName { get; set; } = "";

        [Required]
        public string Degree { get; set; } = "";

        [Required]
        public string Phone { get; set; } = "";

        [Required, Display(Name = "User ID")]
        public string UserId { get; set; } = "";

        [Required, MinLength(8)]
        public string Password { get; set; } = "";

        [Required, Display(Name = "Confirm password"), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = "";

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The PIN must be 6 digits.")]
        public string Pin { get; set; } = "";

        [Required, Display(Name = "Confirm PIN"), Compare(nameof(Pin))]
        public string ConfirmPin { get; set; } = "";
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.FullName = Input.FullName.Trim();
        Input.Degree = Input.Degree.Trim();
        Input.Phone = Input.Phone.Trim();
        Input.UserId = Input.UserId.Trim();
        Input.Pin = Input.Pin.Trim();

        if (PhoneNumbers.Digits(Input.Phone).Length < 10)
            ModelState.AddModelError("Input.Phone", "Enter a phone number with at least 10 digits.");
        if (!PinRules.SixDigits().IsMatch(Input.Pin))
            ModelState.AddModelError("Input.Pin", "The PIN must be 6 digits.");
        if (!ModelState.IsValid)
            return Page();

        foreach (var existing in await _users.Users.ToListAsync())
        {
            if (string.IsNullOrEmpty(existing.PinHash))
                continue;
            if (_pins.VerifyHashedPassword(existing, existing.PinHash, Input.Pin) != PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError("Input.Pin", "That PIN is already used. Choose another.");
                return Page();
            }
        }

        var doctor = new ApplicationUser
        {
            UserName = Input.UserId,
            FullName = Input.FullName,
            Degree = Input.Degree,
            PhoneNumber = Input.Phone
        };
        var created = await _users.CreateAsync(doctor, Input.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        doctor.PinHash = _pins.HashPassword(doctor, Input.Pin);
        await _users.UpdateAsync(doctor);
        await _signIn.SignInAsync(doctor, isPersistent: true);
        return RedirectToPage("/Account/Security");
    }
}
