using Microsoft.AspNetCore.Identity;

namespace DoctorApp.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string Degree { get; set; } = "";
    public string PinHash { get; set; } = "";
}

public class WebAuthnCredential
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public string CredentialId { get; set; } = "";
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public long SignCount { get; set; }
    public string? CredType { get; set; }
    public Guid AaGuid { get; set; }
    public string DeviceLabel { get; set; } = "This device";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DiseaseCategory
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icd11 { get; set; } = "";
    public string Citation { get; set; } = "";
    public int SortOrder { get; set; }
    public List<DiseaseSubcategory> Subcategories { get; set; } = new();
}

public class DiseaseSubcategory
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public DiseaseCategory? Category { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string? Icd11 { get; set; }
    public int SortOrder { get; set; }
}

public class CatalogProduct
{
    public int Id { get; set; }
    public string SourceKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BrandName { get; set; }
    public string? CompanyName { get; set; }
    public string? DoseForm { get; set; }
    public string Composition { get; set; } = "";
    public string? Pack { get; set; }
    public decimal? MrpInr { get; set; }
    public string? PriceAsOf { get; set; }
    public string? PriceSource { get; set; }
    public string? CareLevels { get; set; }
    public string? SectionCode { get; set; }
    public string? SectionTitle { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceDetail { get; set; } = "";
    public string RetrievedOn { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool IsCustom { get; set; }
    public string? OwnerDoctorId { get; set; }
    public List<ProductTag> Tags { get; set; } = new();
}

public class ProductTag
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public CatalogProduct? Product { get; set; }
    public string Label { get; set; } = "";
    public string Origin { get; set; } = "inferred";
    public int? SubcategoryId { get; set; }
    public DiseaseSubcategory? Subcategory { get; set; }
    public string? Note { get; set; }
}

public class DiagnosticTest
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Patient
{
    public int Id { get; set; }
    public string DoctorId { get; set; } = "";
    public ApplicationUser? Doctor { get; set; }
    public string Name { get; set; } = "";
    public string Age { get; set; } = "";
    public decimal? WeightKg { get; set; }
    public string? Phone { get; set; }
    public string? LatestBloodPressure { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Prescription> Prescriptions { get; set; } = new();
}

public class Prescription
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient? Patient { get; set; }
    public string DoctorId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientAge { get; set; } = "";
    public decimal PatientWeightKg { get; set; }
    public string BloodPressure { get; set; } = "";
    public string? PatientPhone { get; set; }
    public string DoctorName { get; set; } = "";
    public string DoctorDegree { get; set; } = "";
    public string DoctorPhone { get; set; } = "";
    public string? Diagnosis { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PrescriptionItem> Items { get; set; } = new();
    public List<PrescriptionTest> Tests { get; set; } = new();
}

public class PrescriptionItem
{
    public int Id { get; set; }
    public int PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }
    public int? MedicineId { get; set; }
    public string MedicineName { get; set; } = "";
    public string Composition { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Dosage { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Instructions { get; set; } = "";
}

public class PrescriptionTest
{
    public int Id { get; set; }
    public int PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }
    public int? CatalogTestId { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public bool IsCustom { get; set; }
}
