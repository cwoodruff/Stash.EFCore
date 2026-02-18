using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Sample.Models;

namespace Stash.EFCore.Sample.Data;

public class SampleDbContext : DbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Electronics" },
            new Category { Id = 2, Name = "Books" });

        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Laptop", Price = 999.99m, CategoryId = 1 },
            new Product { Id = 2, Name = "Phone", Price = 699.99m, CategoryId = 1 },
            new Product { Id = 3, Name = "C# in Depth", Price = 39.99m, CategoryId = 2 });
    }
}
