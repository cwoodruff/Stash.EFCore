using Microsoft.EntityFrameworkCore;

namespace Stash.EFCore.Benchmarks.Infrastructure;

public class BenchmarkProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int CategoryId { get; set; }
    public BenchmarkCategory Category { get; set; } = null!;
}

public class BenchmarkCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<BenchmarkProduct> Products { get; set; } = [];
}

public class BenchmarkDbContext : DbContext
{
    public DbSet<BenchmarkProduct> Products => Set<BenchmarkProduct>();
    public DbSet<BenchmarkCategory> Categories => Set<BenchmarkCategory>();

    public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BenchmarkProduct>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId);
    }
}
