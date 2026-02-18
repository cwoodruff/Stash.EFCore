using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Interceptors;
using Stash.EFCore.Tests.Fakes;
using Xunit;

namespace Stash.EFCore.Tests.Integration;

public class ConfigurationTests : IDisposable
{
    private readonly CacheTestFixture _f;

    public ConfigurationTests()
    {
        _f = new CacheTestFixture();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task DefaultOptions_WorkOutOfBox()
    {
        // Default fixture has no special config — .Cached() should still work
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        var result2 = await ctx.Products.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "default config should cache .Cached() queries");
    }

    [Fact]
    public async Task CustomKeyPrefix_StillCachesCorrectly()
    {
        var f = new CacheTestFixture(opts => opts.KeyPrefix = "myapp:");
        using var ctx = f.CreateContext();

        f.SqlCounter.Reset();
        await ctx.Products.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        await ctx.Products.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "custom key prefix should work");

        f.Dispose();
    }

    [Fact]
    public async Task ExcludedTables_NotCachedInCacheAllMode()
    {
        var f = new CacheTestFixture(opts =>
        {
            opts.CacheAllQueries = true;
            opts.ExcludedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AppSettings" };
        });

        using var ctx = f.CreateContext();

        // Non-excluded table should be cached
        f.SqlCounter.Reset();
        await ctx.Products.ToListAsync(); // no .Cached(), relies on CacheAllQueries
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        await ctx.Products.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "non-excluded table should be cached");

        // Excluded table should NOT be cached
        f.SqlCounter.Reset();
        await ctx.AppSettings.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        await ctx.AppSettings.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "excluded table should not be cached");

        f.Dispose();
    }

    [Fact]
    public async Task CacheAllQueries_CachesEverything()
    {
        var f = new CacheTestFixture(opts => opts.CacheAllQueries = true);
        using var ctx = f.CreateContext();

        // No .Cached() needed — CacheAllQueries caches everything
        f.SqlCounter.Reset();
        await ctx.Products.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        await ctx.Products.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "CacheAllQueries should auto-cache all SELECTs");

        f.SqlCounter.Reset();
        await ctx.Categories.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "first call to different table hits DB");

        f.SqlCounter.Reset();
        await ctx.Categories.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "subsequent call to categories from cache");

        f.Dispose();
    }

    [Fact]
    public async Task FallbackToDatabase_WhenCacheThrows()
    {
        // Create a fixture with a failing cache store
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30),
            FallbackToDatabase = true
        };

        var failingStore = new FailingCacheStore();
        var keyGen = new DefaultCacheKeyGenerator(options);
        var commandInterceptor = new StashCommandInterceptor(
            failingStore, keyGen, options, NullLogger<StashCommandInterceptor>.Instance);
        var counter = new SqlCountingInterceptor();

        var dbOptions = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(commandInterceptor, counter)
            .Options;

        using var setupCtx = new IntegrationDbContext(dbOptions);
        setupCtx.Database.EnsureCreated();
        setupCtx.Categories.Add(new Category { Name = "TestCat" });
        setupCtx.SaveChanges();
        setupCtx.ChangeTracker.Clear();
        counter.Reset();

        // Query should fall back to DB despite cache store throwing
        using var ctx = new IntegrationDbContext(dbOptions);
        var result = await ctx.Categories.Cached().ToListAsync();
        result.Should().HaveCount(1, "should fall back to DB when cache throws");
        counter.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task FallbackToDatabase_False_CacheThrows_ExceptionPropagates()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30),
            FallbackToDatabase = false
        };

        var failingStore = new FailingCacheStore();
        var keyGen = new DefaultCacheKeyGenerator(options);
        var commandInterceptor = new StashCommandInterceptor(
            failingStore, keyGen, options, NullLogger<StashCommandInterceptor>.Instance);

        // Create and seed without the failing interceptor
        var setupOptions = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlite(conn)
            .Options;
        using (var setupCtx = new IntegrationDbContext(setupOptions))
        {
            setupCtx.Database.EnsureCreated();
            setupCtx.Categories.Add(new Category { Name = "TestCat" });
            setupCtx.SaveChanges();
        }

        var dbOptions = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(commandInterceptor)
            .Options;

        using var ctx = new IntegrationDbContext(dbOptions);
        var act = () => ctx.Categories.Cached().ToListAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cache unavailable*");
    }

    [Fact]
    public async Task WithoutCachedTag_NoCacheAllQueries_QueriesNotCached()
    {
        // Default fixture has CacheAllQueries=false
        using var ctx = _f.CreateContext();

        // Query without .Cached() should NOT be cached
        _f.SqlCounter.Reset();
        await ctx.Products.ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        await ctx.Products.ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1, "without .Cached() or CacheAllQueries, queries hit DB every time");
    }

    [Fact]
    public async Task ExcludedTable_ExplicitCached_StillWorks()
    {
        // Even when a table is excluded from CacheAllQueries,
        // explicit .Cached() should override and cache it
        var f = new CacheTestFixture(opts =>
        {
            opts.CacheAllQueries = true;
            opts.ExcludedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AppSettings" };
        });

        using var ctx = f.CreateContext();

        // Explicit .Cached() should override exclusion
        f.SqlCounter.Reset();
        await ctx.AppSettings.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        await ctx.AppSettings.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "explicit .Cached() should override ExcludedTables");

        f.Dispose();
    }
}
