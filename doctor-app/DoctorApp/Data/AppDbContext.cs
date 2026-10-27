using DoctorApp.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DoctorApp.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<WebAuthnCredential> WebAuthnCredentials => Set<WebAuthnCredential>();
    public DbSet<DiseaseCategory> DiseaseCategories => Set<DiseaseCategory>();
    public DbSet<DiseaseSubcategory> DiseaseSubcategories => Set<DiseaseSubcategory>();
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<DiagnosticTest> DiagnosticTests => Set<DiagnosticTest>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<PrescriptionTest> PrescriptionTests => Set<PrescriptionTest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DiseaseCategory>().HasIndex(c => c.Slug).IsUnique();
        builder.Entity<DiseaseSubcategory>().HasIndex(s => s.Slug).IsUnique();
        builder.Entity<DiseaseSubcategory>()
            .HasOne(s => s.Category)
            .WithMany(c => c.Subcategories)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CatalogProduct>().HasIndex(p => p.SourceKey).IsUnique();
        builder.Entity<CatalogProduct>().HasIndex(p => p.CompanyName);
        builder.Entity<CatalogProduct>().HasIndex(p => p.Name);

        builder.Entity<ProductTag>()
            .HasOne(t => t.Product)
            .WithMany(p => p.Tags)
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProductTag>()
            .HasOne(t => t.Subcategory)
            .WithMany()
            .HasForeignKey(t => t.SubcategoryId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProductTag>().HasIndex(t => t.SubcategoryId);
        builder.Entity<ProductTag>().HasIndex(t => t.Label);

        builder.Entity<WebAuthnCredential>().HasIndex(c => c.CredentialId).IsUnique();
        builder.Entity<WebAuthnCredential>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Patient>()
            .HasOne(p => p.Doctor)
            .WithMany()
            .HasForeignKey(p => p.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Prescription>()
            .HasOne(p => p.Patient)
            .WithMany(p => p.Prescriptions)
            .HasForeignKey(p => p.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PrescriptionItem>()
            .HasOne(i => i.Prescription)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PrescriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PrescriptionTest>()
            .HasOne(t => t.Prescription)
            .WithMany(p => p.Tests)
            .HasForeignKey(t => t.PrescriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
