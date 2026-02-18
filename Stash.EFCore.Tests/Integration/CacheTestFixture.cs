using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Tests.Integration;

#region Entities

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Entity with various column types for edge-case testing.
/// </summary>
public class AllTypesEntity
{
    public int Id { get; set; }
    public string StringVal { get; set; } = "";
    public int IntVal { get; set; }
    public long LongVal { get; set; }
    public double DoubleVal { get; set; }
    public decimal DecimalVal { get; set; }
    public bool BoolVal { get; set; }
    public DateTime DateTimeVal { get; set; }
    public byte[] BlobVal { get; set; } = [];
    public string? NullableStringVal { get; set; }
}

public class IntegrationDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AllTypesEntity> AllTypes => Set<AllTypesEntity>();

    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId);

        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany()
            .HasForeignKey(oi => oi.ProductId);
    }
}

#endregion

#region SQL execution counter

/// <summary>
/// Interceptor that counts actual SQL executions (reader + scalar).
/// Must be added AFTER <see cref="StashCommandInterceptor"/> in the chain
/// so that cache hits appear as <c>result.HasResult == true</c>.
/// </summary>
internal sealed class SqlCountingInterceptor : DbCommandInterceptor
{
    private int _executionCount;

    public int ExecutionCount => _executionCount;

    public void Reset() => Interlocked.Exchange(ref _executionCount, 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (!result.HasResult)
            Interlocked.Increment(ref _executionCount);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        if (!result.HasResult)
            Interlocked.Increment(ref _executionCount);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        if (!result.HasResult)
            Interlocked.Increment(ref _executionCount);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        if (!result.HasResult)
            Interlocked.Increment(ref _executionCount);
        return result;
    }
}

#endregion

#region Test fixture

/// <summary>
/// Shared test fixture that provides an isolated SQLite in-memory database
/// with Stash.EFCore caching configured, seed data, and a SQL execution counter.
/// Each xUnit test class instance gets its own fixture (fresh cache and DB state).
/// </summary>
internal sealed class CacheTestFixture : IDisposable
{
    public SqliteConnection Connection { get; }
    public StashOptions Options { get; }
    public MemoryCacheStore CacheStore { get; }
    public StashCommandInterceptor CommandInterceptor { get; }
    public StashInvalidationInterceptor InvalidationInterceptor { get; }
    public SqlCountingInterceptor SqlCounter { get; }

    private readonly DbContextOptions<IntegrationDbContext> _contextOptions;

    public CacheTestFixture(Action<StashOptions>? configure = null)
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        Options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30)
        };
        configure?.Invoke(Options);

        CacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
        var keyGen = new DefaultCacheKeyGenerator(Options);

        CommandInterceptor = new StashCommandInterceptor(
            CacheStore, keyGen, Options, NullLogger<StashCommandInterceptor>.Instance);
        InvalidationInterceptor = new StashInvalidationInterceptor(
            CacheStore, NullLogger<StashInvalidationInterceptor>.Instance);
        SqlCounter = new SqlCountingInterceptor();

        _contextOptions = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlite(Connection)
            .AddInterceptors(CommandInterceptor, InvalidationInterceptor, SqlCounter)
            .Options;

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
        SeedData(ctx);
        SqlCounter.Reset();
    }

    public IntegrationDbContext CreateContext() => new(_contextOptions);

    private static void SeedData(IntegrationDbContext ctx)
    {
        // 5 categories
        var categories = new[]
        {
            new Category { Name = "Electronics" },
            new Category { Name = "Books" },
            new Category { Name = "Clothing" },
            new Category { Name = "Food" },
            new Category { Name = "Sports" }
        };
        ctx.Categories.AddRange(categories);
        ctx.SaveChanges();

        // 50 products (10 per category)
        for (var c = 0; c < categories.Length; c++)
        {
            for (var p = 0; p < 10; p++)
            {
                var idx = c * 10 + p + 1;
                ctx.Products.Add(new Product
                {
                    Name = $"Product-{idx}",
                    Price = 10.00m + idx,
                    IsActive = idx % 5 != 0, // every 5th product is inactive
                    CategoryId = categories[c].Id
                });
            }
        }
        ctx.SaveChanges();

        // 10 orders with items
        var rng = new Random(42); // deterministic seed
        for (var o = 0; o < 10; o++)
        {
            var order = new Order
            {
                CustomerName = $"Customer-{o + 1}",
                OrderDate = new DateTime(2025, 1, 1).AddDays(o)
            };
            ctx.Orders.Add(order);
            ctx.SaveChanges();

            var itemCount = rng.Next(1, 4); // 1-3 items
            for (var i = 0; i < itemCount; i++)
            {
                var productId = rng.Next(1, 51); // products 1-50
                ctx.OrderItems.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = productId,
                    Quantity = rng.Next(1, 6),
                    UnitPrice = 10.00m + productId
                });
            }
            ctx.SaveChanges();
        }

        // 3 app settings
        ctx.AppSettings.AddRange(
            new AppSetting { Key = "Theme", Value = "Dark" },
            new AppSetting { Key = "PageSize", Value = "25" },
            new AppSetting { Key = "CacheTTL", Value = "300" }
        );
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}

#endregion
