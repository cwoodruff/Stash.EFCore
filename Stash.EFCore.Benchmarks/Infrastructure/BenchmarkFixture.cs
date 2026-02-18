using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Benchmarks.Infrastructure;

/// <summary>
/// Shared benchmark infrastructure providing an isolated SQLite in-memory database
/// with Stash.EFCore caching configured and configurable seed data.
/// </summary>
public sealed class BenchmarkFixture : IDisposable
{
    public SqliteConnection Connection { get; }
    public StashOptions Options { get; }
    public MemoryCacheStore CacheStore { get; }
    public DefaultCacheKeyGenerator KeyGenerator { get; }
    public StashCommandInterceptor CommandInterceptor { get; }
    public StashInvalidationInterceptor InvalidationInterceptor { get; }

    private readonly DbContextOptions<BenchmarkDbContext> _cachedContextOptions;
    private readonly DbContextOptions<BenchmarkDbContext> _baselineContextOptions;

    public BenchmarkFixture(int productCount = 100, Action<StashOptions>? configure = null)
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        Options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30),
            EnableLogging = false // suppress logging in benchmarks
        };
        configure?.Invoke(Options);

        CacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 256 * 1024 * 1024 // 256 MB
        }));
        KeyGenerator = new DefaultCacheKeyGenerator(Options);

        CommandInterceptor = new StashCommandInterceptor(
            CacheStore, KeyGenerator, Options, NullLogger<StashCommandInterceptor>.Instance);
        InvalidationInterceptor = new StashInvalidationInterceptor(
            CacheStore, NullLogger<StashInvalidationInterceptor>.Instance);

        _cachedContextOptions = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite(Connection)
            .AddInterceptors(CommandInterceptor, InvalidationInterceptor)
            .Options;

        _baselineContextOptions = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite(Connection)
            .Options;

        using var ctx = CreateCachedContext();
        ctx.Database.EnsureCreated();
        SeedData(ctx, productCount);
    }

    /// <summary>Creates a DbContext with Stash interceptors enabled.</summary>
    public BenchmarkDbContext CreateCachedContext() => new(_cachedContextOptions);

    /// <summary>Creates a DbContext without Stash interceptors (baseline).</summary>
    public BenchmarkDbContext CreateBaselineContext() => new(_baselineContextOptions);

    /// <summary>Clears the cache store (invalidates all entries).</summary>
    public async Task ClearCacheAsync() => await CacheStore.InvalidateAllAsync();

    private static void SeedData(BenchmarkDbContext ctx, int productCount)
    {
        var categories = new[]
        {
            new BenchmarkCategory { Name = "Electronics" },
            new BenchmarkCategory { Name = "Books" },
            new BenchmarkCategory { Name = "Clothing" },
            new BenchmarkCategory { Name = "Home" },
            new BenchmarkCategory { Name = "Sports" }
        };
        ctx.Categories.AddRange(categories);
        ctx.SaveChanges();

        for (var i = 0; i < productCount; i++)
        {
            ctx.Products.Add(new BenchmarkProduct
            {
                Name = $"Product-{i + 1:D5}",
                Price = 5.00m + (i * 0.50m),
                IsActive = i % 7 != 0,
                CategoryId = categories[i % categories.Length].Id
            });
        }
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
    }

    public void Dispose() => Connection.Dispose();
}
