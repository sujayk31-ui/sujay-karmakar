using System.Text;
using System.Text.Json;
using DoctorApp.Data;
using DoctorApp.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Controllers;

[ApiController]
[Route("webauthn")]
[ValidateAntiForgeryToken]
public class WebAuthnController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly AppDbContext _db;

    public WebAuthnController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AppDbContext db)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
    }

    [HttpPost("register-options")]
    [Authorize]
    public async Task<IActionResult> RegisterOptions()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return Unauthorized();
        var existing = await _db.WebAuthnCredentials.Where(c => c.UserId == doctor.Id).ToListAsync();
        var exclude = existing
            .Select(c => new PublicKeyCredentialDescriptor(Base64Url.Decode(c.CredentialId)))
            .ToList();
        var options = Fido().RequestNewCredential(
            new Fido2User
            {
                DisplayName = doctor.FullName,
                Name = doctor.UserName ?? doctor.Id,
                Id = Encoding.UTF8.GetBytes(doctor.Id)
            },
            exclude,
            new AuthenticatorSelection
            {
                AuthenticatorAttachment = AuthenticatorAttachment.Platform,
                RequireResidentKey = true,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationConveyancePreference.None,
            null);
        HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());
        return Content(options.ToJson(), "application/json");
    }

    public sealed class EnrollRequest
    {
        public string? DeviceLabel { get; set; }
        public AuthenticatorAttestationRawResponse? Credential { get; set; }
    }

    [HttpPost("register")]
    [Authorize]
    public async Task<IActionResult> Register([FromBody] EnrollRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var doctor = await _users.GetUserAsync(User) ?? throw new InvalidOperationException("Sign in first.");
            var json = HttpContext.Session.GetString("fido2.attestationOptions")
                ?? throw new InvalidOperationException("Enrollment session expired. Try again.");
            var options = CredentialCreateOptions.FromJson(json);
            if (body.Credential == null)
                throw new InvalidOperationException("The browser did not return a credential.");

            IsCredentialIdUniqueToUserAsyncDelegate callback = async (args, ct) =>
            {
                var id = Base64Url.Encode(args.CredentialId);
                return !await _db.WebAuthnCredentials.AnyAsync(c => c.CredentialId == id, ct);
            };
            var success = await Fido().MakeNewCredentialAsync(body.Credential, options, callback, cancellationToken: cancellationToken);
            var result = success.Result ?? throw new InvalidOperationException("Fingerprint enrollment was not verified.");
            _db.WebAuthnCredentials.Add(new WebAuthnCredential
            {
                UserId = doctor.Id,
                CredentialId = Base64Url.Encode(result.CredentialId),
                PublicKey = result.PublicKey,
                SignCount = result.Counter,
                CredType = result.CredType,
                AaGuid = result.Aaguid,
                DeviceLabel = string.IsNullOrWhiteSpace(body.DeviceLabel) ? "This device" : body.DeviceLabel.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            HttpContext.Session.Remove("fido2.attestationOptions");
            return Json(new { status = "ok" });
        }
        catch (Exception ex)
        {
            return Json(new { status = "error", errorMessage = ex.Message });
        }
    }

    [HttpPost("login-options")]
    [AllowAnonymous]
    public IActionResult LoginOptions()
    {
        var options = Fido().GetAssertionOptions(
            Array.Empty<PublicKeyCredentialDescriptor>(),
            UserVerificationRequirement.Required);
        HttpContext.Session.SetString("fido2.assertionOptions", options.ToJson());
        return Content(options.ToJson(), "application/json");
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] AuthenticatorAssertionRawResponse clientResponse, CancellationToken cancellationToken)
    {
        try
        {
            var json = HttpContext.Session.GetString("fido2.assertionOptions")
                ?? throw new InvalidOperationException("Sign-in session expired. Try again.");
            var options = AssertionOptions.FromJson(json);
            var credentialId = Base64Url.Encode(clientResponse.Id);
            var stored = await _db.WebAuthnCredentials.FirstOrDefaultAsync(c => c.CredentialId == credentialId, cancellationToken)
                ?? throw new InvalidOperationException("This fingerprint is not enrolled on Vaidya.");

            IsUserHandleOwnerOfCredentialIdAsync callback = async (args, ct) =>
            {
                var cred = await _db.WebAuthnCredentials.FirstOrDefaultAsync(c => c.CredentialId == Base64Url.Encode(args.CredentialId), ct);
                if (cred == null)
                    return false;
                if (args.UserHandle == null || args.UserHandle.Length == 0)
                    return true;
                return Encoding.UTF8.GetBytes(cred.UserId).SequenceEqual(args.UserHandle);
            };

            var verified = await Fido().MakeAssertionAsync(
                clientResponse,
                options,
                stored.PublicKey,
                (uint)stored.SignCount,
                callback,
                cancellationToken: cancellationToken);
            stored.SignCount = verified.Counter;
            await _db.SaveChangesAsync(cancellationToken);
            var doctor = await _users.FindByIdAsync(stored.UserId)
                ?? throw new InvalidOperationException("Doctor account for this fingerprint was not found.");
            await _signIn.SignInAsync(doctor, isPersistent: true);
            HttpContext.Session.Remove("fido2.assertionOptions");
            return Json(new { status = "ok" });
        }
        catch (Exception ex)
        {
            return Json(new { status = "error", errorMessage = ex.Message });
        }
    }

    private Fido2NetLib.Fido2 Fido()
    {
        var origin = $"{Request.Scheme}://{Request.Host.Value}";
        return new Fido2NetLib.Fido2(new Fido2Configuration
        {
            ServerDomain = Request.Host.Host,
            ServerName = "Vaidya",
            Origins = new HashSet<string> { origin },
            TimestampDriftTolerance = 300000
        });
    }
}
