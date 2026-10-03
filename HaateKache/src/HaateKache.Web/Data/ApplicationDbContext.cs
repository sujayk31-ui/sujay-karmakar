using HaateKache.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HaateKache.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Installment> Installments => Set<Installment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Store>(entity =>
        {
            entity.HasIndex(s => s.Slug).IsUnique();
            entity.HasIndex(s => s.OwnerId).IsUnique();
            entity.Property(s => s.Name).HasMaxLength(120);
            entity.HasOne(s => s.Owner)
                .WithMany()
                .HasForeignKey(s => s.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.Slug).IsUnique();
            entity.Property(c => c.Name).HasMaxLength(40);
        });

        builder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Price).HasPrecision(12, 2);
            entity.HasOne(p => p.Store)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.StoreId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.OrderNumber).IsUnique();
            entity.Property(o => o.Subtotal).HasPrecision(12, 2);
            entity.Property(o => o.Total).HasPrecision(12, 2);
            entity.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(o => o.Store)
                .WithMany()
                .HasForeignKey(o => o.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.UnitPrice).HasPrecision(12, 2);
            entity.Property(l => l.LineTotal).HasPrecision(12, 2);
            entity.HasOne(l => l.Order)
                .WithMany(o => o.Lines)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Payment>(entity =>
        {
            entity.Property(p => p.AmountAuthorized).HasPrecision(12, 2);
            entity.Property(p => p.CardLast4).HasMaxLength(4);
            entity.HasOne(p => p.Order)
                .WithOne(o => o.Payment)
                .HasForeignKey<Payment>(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Installment>(entity =>
        {
            entity.Property(i => i.Amount).HasPrecision(12, 2);
            entity.HasOne(i => i.Order)
                .WithMany(o => o.Installments)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
