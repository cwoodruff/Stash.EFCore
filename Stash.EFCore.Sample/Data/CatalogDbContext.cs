using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Sample.Models;

namespace Stash.EFCore.Sample.Data;

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId);

            e.HasOne(p => p.Supplier)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.SupplierId);

            e.Property(p => p.Price).HasColumnType("decimal(10,2)");
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // 5 Categories
        var categories = new[]
        {
            new Category { Id = 1, Name = "Electronics", Description = "Gadgets and devices" },
            new Category { Id = 2, Name = "Books", Description = "Physical and digital books" },
            new Category { Id = 3, Name = "Clothing", Description = "Apparel and accessories" },
            new Category { Id = 4, Name = "Home & Garden", Description = "Home improvement and gardening" },
            new Category { Id = 5, Name = "Sports", Description = "Sporting goods and equipment" }
        };
        modelBuilder.Entity<Category>().HasData(categories);

        // 3 Suppliers
        var suppliers = new[]
        {
            new Supplier { Id = 1, Name = "TechCorp", ContactEmail = "sales@techcorp.example" },
            new Supplier { Id = 2, Name = "BookWorld", ContactEmail = "orders@bookworld.example" },
            new Supplier { Id = 3, Name = "MegaSupply", ContactEmail = "info@megasupply.example" }
        };
        modelBuilder.Entity<Supplier>().HasData(suppliers);

        // 100 Products spread across categories and suppliers
        var products = new Product[100];
        var names = new[]
        {
            "Widget", "Gadget", "Doohickey", "Thingamajig", "Gizmo",
            "Contraption", "Apparatus", "Device", "Instrument", "Mechanism"
        };

        for (var i = 0; i < 100; i++)
        {
            products[i] = new Product
            {
                Id = i + 1,
                Name = $"{names[i % names.Length]} {i + 1:D3}",
                Price = Math.Round(5.00m + (i * 2.50m), 2),
                IsActive = i % 7 != 0, // ~14% inactive
                CategoryId = (i % 5) + 1,
                SupplierId = (i % 3) + 1
            };
        }
        modelBuilder.Entity<Product>().HasData(products);
    }
}
