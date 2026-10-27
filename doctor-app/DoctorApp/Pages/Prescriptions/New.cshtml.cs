using DoctorApp.Data;
using DoctorApp.Models;
using DoctorApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Pages.Prescriptions;

public class NewModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly ClinicSearch _search;

    public NewModel(UserManager<ApplicationUser> users, AppDbContext db, ClinicSearch search)
    {
        _users = users;
        _db = db;
        _search = search;
    }

    public ApplicationUser Doctor { get; private set; } = new();
    public List<TestGroup> TestGroups { get; private set; } = new();

    [BindProperty] public string PatientName { get; set; } = "";
    [BindProperty] public string PatientAge { get; set; } = "";
    [BindProperty] public decimal? WeightKg { get; set; }
    [BindProperty] public string BloodPressure { get; set; } = "";
    [BindProperty] public string? PatientPhone { get; set; }
    [BindProperty] public string? Diagnosis { get; set; }
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public List<MedicineLine> Lines { get; set; } = new();
    [BindProperty] public List<int> TestIds { get; set; } = new();
    [BindProperty] public string? CustomTests { get; set; }

    public class MedicineLine
    {
        public string? MedicineId { get; set; }
        public string? MedicineName { get; set; }
        public string? Composition { get; set; }
        public string? CompanyName { get; set; }
        public string? Dosage { get; set; }
        public string? Duration { get; set; }
        public string? Instructions { get; set; }
    }

    public class TestGroup
    {
        public string Category { get; set; } = "";
        public List<DiagnosticTest> Tests { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        Doctor = doctor;
        if (Lines.Count == 0)
            Lines.Add(new MedicineLine());
        await LoadTestsAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetSuggestAsync(string? q)
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return new JsonResult(Array.Empty<object>());
        var query = (q ?? "").Trim().ToLowerInvariant();
        if (query.Length < 2)
            return new JsonResult(Array.Empty<object>());
        var hits = await _db.CatalogProducts.AsNoTracking()
            .Where(p => !p.IsCustom || p.OwnerDoctorId == doctor.Id)
            .Where(p => p.Name.ToLower().Contains(query)
                || p.Composition.ToLower().Contains(query)
                || (p.BrandName != null && p.BrandName.ToLower().Contains(query)))
            .OrderBy(p => p.Name)
            .Take(12)
            .Select(p => new { id = p.Id, name = p.Name, composition = p.Composition, company = p.CompanyName, price = p.MrpInr })
            .ToListAsync();
        return new JsonResult(hits);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var doctor = await _users.GetUserAsync(User);
        if (doctor == null)
            return RedirectToPage("/Account/Login");
        Doctor = doctor;
        PatientName = PatientName.Trim();
        PatientAge = PatientAge.Trim();
        BloodPressure = (BloodPressure ?? "").Trim();
        PatientPhone = string.IsNullOrWhiteSpace(PatientPhone) ? null : PatientPhone.Trim();
        Diagnosis = string.IsNullOrWhiteSpace(Diagnosis) ? null : Diagnosis.Trim();
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        if (PatientName.Length == 0)
            ModelState.AddModelError(nameof(PatientName), "Enter the patient name.");
        if (PatientAge.Length == 0)
            ModelState.AddModelError(nameof(PatientAge), "Enter the age, for example 8 months or 34 years.");
        if (PatientAge.Length > 40)
            ModelState.AddModelError(nameof(PatientAge), "Keep the age under 40 characters.");
        if (WeightKg is null || WeightKg < 0.1m || WeightKg > 700m)
            ModelState.AddModelError(nameof(WeightKg), "Enter the weight in kilograms.");
        if (BloodPressure.Length > 80)
            ModelState.AddModelError(nameof(BloodPressure), "Keep the blood pressure note under 80 characters.");

        var items = new List<PrescriptionItem>();
        foreach (var line in Lines)
        {
            CatalogProduct? product = null;
            if (int.TryParse(line.MedicineId, out var id))
                product = await _search.Visible(doctor.Id).FirstOrDefaultAsync(p => p.Id == id);
            var name = (product?.Name ?? line.MedicineName ?? "").Trim();
            if (name.Length == 0)
                continue;
            items.Add(new PrescriptionItem
            {
                MedicineId = product?.Id,
                MedicineName = name,
                Composition = (product?.Composition ?? line.Composition ?? "").Trim(),
                CompanyName = product?.CompanyName ?? NullIfBlank(line.CompanyName),
                Dosage = (line.Dosage ?? "").Trim(),
                Duration = (line.Duration ?? "").Trim(),
                Instructions = (line.Instructions ?? "").Trim()
            });
        }

        var catalogIds = TestIds.Distinct().ToList();
        var catalogTests = catalogIds.Count == 0
            ? new List<DiagnosticTest>()
            : await _db.DiagnosticTests.AsNoTracking().Where(t => catalogIds.Contains(t.Id)).ToListAsync();
        var customNames = (CustomTests ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0 && name.Length <= 120)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
        if (items.Count == 0 && catalogTests.Count == 0 && customNames.Count == 0)
            ModelState.AddModelError(string.Empty, "Add a medicine or a test.");
        if (!ModelState.IsValid)
        {
            if (Lines.Count == 0)
                Lines.Add(new MedicineLine());
            await LoadTestsAsync();
            return Page();
        }

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.DoctorId == doctor.Id && p.Name.ToLower() == PatientName.ToLower());
        if (patient == null)
        {
            patient = new Patient { DoctorId = doctor.Id, Name = PatientName, CreatedAt = DateTime.UtcNow };
            _db.Patients.Add(patient);
        }
        patient.Age = PatientAge;
        patient.WeightKg = WeightKg;
        patient.Phone = PatientPhone;
        patient.LatestBloodPressure = string.IsNullOrEmpty(BloodPressure) ? null : BloodPressure;

        var rx = new Prescription
        {
            Patient = patient,
            DoctorId = doctor.Id,
            PatientName = PatientName,
            PatientAge = PatientAge,
            PatientWeightKg = WeightKg!.Value,
            BloodPressure = BloodPressure,
            PatientPhone = PatientPhone,
            DoctorName = doctor.FullName,
            DoctorDegree = doctor.Degree,
            DoctorPhone = doctor.PhoneNumber ?? "",
            Diagnosis = Diagnosis,
            Notes = Notes,
            CreatedAt = DateTime.UtcNow,
            Items = items,
            Tests = catalogTests.Select(test => new PrescriptionTest
            {
                CatalogTestId = test.Id,
                Name = test.Name,
                Category = test.Category,
                IsCustom = false
            }).Concat(customNames.Select(name => new PrescriptionTest
            {
                Name = name,
                IsCustom = true
            })).ToList()
        };
        _db.Prescriptions.Add(rx);
        await _db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = rx.Id });
    }

    private async Task LoadTestsAsync()
    {
        var tests = await _db.DiagnosticTests.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
        TestGroups = tests.GroupBy(t => t.Category)
            .Select(g => new TestGroup { Category = g.Key, Tests = g.ToList() })
            .ToList();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
