using System.Text.RegularExpressions;
using HaateKache.Web.Data;
using HaateKache.Web.Models;
using HaateKache.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ApplicationDbContext _db;

    public AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ApplicationDbContext db)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.Email = model.Email.Trim();
        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            model.Password = "";
            model.Error = result.IsLockedOut
                ? "This account is locked for a few minutes."
                : "Those credentials were not recognized.";
            return View(model);
        }

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return LocalRedirect(model.ReturnUrl);

        var user = await _users.FindByEmailAsync(model.Email);
        if (user is not null && await _users.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Dashboard", "Admin");
        if (user is not null && await _users.IsInRoleAsync(user, "Vendor"))
            return RedirectToAction("Dashboard", "Vendor");
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        var error = ValidatePerson(model.FullName, model.Email, model.Password, model.ConfirmPassword);
        if (error is not null)
        {
            model.Error = error;
            model.Password = "";
            model.ConfirmPassword = "";
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),
            PhoneNumber = model.Phone.Trim()
        };
        var result = await _users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            model.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            model.Password = "";
            model.ConfirmPassword = "";
            return View(model);
        }

        await _users.AddToRoleAsync(user, "Customer");
        await _signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult VendorRegister() => View(new VendorRegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VendorRegister(VendorRegisterViewModel model)
    {
        var error = ValidatePerson(model.FullName, model.Email, model.Password, model.ConfirmPassword);
        if (error is null && string.IsNullOrWhiteSpace(model.StoreName))
            error = "Give the store a name.";
        if (error is null && string.IsNullOrWhiteSpace(model.Address))
            error = "Enter the pickup address.";
        if (error is null && string.IsNullOrWhiteSpace(model.Area))
            error = "Enter the neighborhood.";
        if (error is null && model.Latitude is < -90 or > 90)
            error = "Latitude must be between -90 and 90.";
        if (error is null && model.Longitude is < -180 or > 180)
            error = "Longitude must be between -180 and 180.";
        if (error is not null)
        {
            model.Error = error;
            model.Password = "";
            model.ConfirmPassword = "";
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),
            PhoneNumber = model.Phone.Trim()
        };
        var result = await _users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            model.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            model.Password = "";
            model.ConfirmPassword = "";
            return View(model);
        }

        await _users.AddToRoleAsync(user, "Vendor");
        var slug = await UniqueSlugAsync(model.StoreName);
        _db.Stores.Add(new Store
        {
            Name = model.StoreName.Trim(),
            Slug = slug,
            Description = model.Description.Trim(),
            Address = model.Address.Trim(),
            Area = model.Area.Trim(),
            Latitude = model.Latitude,
            Longitude = model.Longitude,
            Phone = model.Phone.Trim(),
            OpeningHours = string.IsNullOrWhiteSpace(model.OpeningHours) ? "9:00–21:00" : model.OpeningHours.Trim(),
            OwnerId = user.Id,
            ApprovalStatus = StoreApprovalStatus.Pending
        });
        await _db.SaveChangesAsync();
        await _signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Dashboard", "Vendor");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Denied() => View();

    private static string? ValidatePerson(string name, string email, string password, string confirm)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter your name.";
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return "Enter a valid email.";
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return "Use a password of at least 8 characters.";
        if (password != confirm)
            return "The passwords do not match.";
        return null;
    }

    private async Task<string> UniqueSlugAsync(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug))
            slug = "store";
        var candidate = slug;
        var suffix = 2;
        while (await _db.Stores.AnyAsync(s => s.Slug == candidate))
        {
            candidate = slug + "-" + suffix;
            suffix++;
        }

        return candidate;
    }
}
