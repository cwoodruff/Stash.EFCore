using System.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Interceptors;
using Stash.EFCore.Tests.Fakes;
using Xunit;

namespace Stash.EFCore.Tests;

#region Test entities and DbContext

public class TestProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class TestOrder
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
}

public class TestDbContext : DbContext
{
    public DbSet<TestProduct> Products => Set<TestProduct>();
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
}

#endregion

public class StashCommandInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MemoryCacheStore _cacheStore;
    private readonly StashOptions _options;
    private readonly StashCommandInterceptor _interceptor;
    private readonly StashInvalidationInterceptor _invalidationInterceptor;
    private readonly TestDbContext _context;

    public StashCommandInterceptorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30)
        };

        var keyGen = new DefaultCacheKeyGenerator(_options);
        _cacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
        var logger = NullLogger<StashCommandInterceptor>.Instance;
        _interceptor = new StashCommandInterceptor(_cacheStore, keyGen, _options, logger);
        _invalidationInterceptor = new StashInvalidationInterceptor(
            _cacheStore, NullLogger<StashInvalidationInterceptor>.Instance, _options);

        var contextOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor, _invalidationInterceptor)
            .Options;

        _context = new TestDbContext(contextOptions);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private void SeedProducts(params string[] names)
    {
        foreach (var name in names)
            _context.Products.Add(new TestProduct { Name = name, Price = 9.99m });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    #region Cache miss → cache hit flow

    [Fact]
    public async Task CacheAllQueries_FirstQueryCacheMiss_SecondQueryCacheHit()
    {
        _options.CacheAllQueries = true;
        SeedProducts("Widget");

        // First query — cache miss, hits DB
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        // Delete from DB directly (bypasses SaveChanges invalidation)
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        // Second query — cache hit, returns stale data from cache
        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1, "result should come from cache, not the empty DB");
        result2[0].Name.Should().Be("Widget");
    }

    [Fact]
    public async Task StashTag_FirstQueryCacheMiss_SecondQueryCacheHit()
    {
        SeedProducts("Gadget");

        var result1 = await _context.Products.Cached().ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.Cached().ToListAsync();
        result2.Should().HaveCount(1, "result should come from cache");
        result2[0].Name.Should().Be("Gadget");
    }

    [Fact]
    public async Task WithoutStashTag_AndCacheAllQueriesFalse_DoesNotCache()
    {
        _options.CacheAllQueries = false;
        SeedProducts("NoCache");

        await _context.Products.ToListAsync();
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result = await _context.Products.ToListAsync();
        result.Should().BeEmpty("query was not tagged and CacheAllQueries is false");
    }

    #endregion

    #region Per-query TTL override

    [Fact]
    public async Task StashWithTtl_UseSpecifiedExpiration()
    {
        var clock = new FakeSystemClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var cacheStore = new MemoryCacheStore(memoryCache);
        var options = new StashOptions();
        var keyGen = new DefaultCacheKeyGenerator(options);
        var interceptor = new StashCommandInterceptor(
            cacheStore, keyGen, options, NullLogger<StashCommandInterceptor>.Instance);

        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var contextOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(interceptor)
            .Options;
        using var context = new TestDbContext(contextOptions);
        context.Database.EnsureCreated();
        context.Products.Add(new TestProduct { Name = "Timed" });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Query with 5-second TTL
        var result1 = await context.Products.Cached(TimeSpan.FromSeconds(5)).ToListAsync();
        result1.Should().HaveCount(1);

        // Advance clock past TTL
        clock.Advance(TimeSpan.FromSeconds(10));

        // Delete from DB
        await context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        // Cache should have expired — query hits empty DB
        var result2 = await context.Products.Cached(TimeSpan.FromSeconds(5)).ToListAsync();
        result2.Should().BeEmpty("cache entry should have expired after 10 seconds");
    }

    [Fact]
    public async Task StashWithProfile_UsesProfileExpiration()
    {
        _options.Profiles["fast-expire"] = new StashProfile
        {
            Name = "fast-expire",
            AbsoluteExpiration = TimeSpan.FromMilliseconds(1)
        };
        SeedProducts("Profiled");

        // First query with profile
        var result1 = await _context.Products.Cached("fast-expire").ToListAsync();
        result1.Should().HaveCount(1);

        // Small delay to ensure TTL expires (MemoryCache checks on access)
        await Task.Delay(50);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        // Cache should have expired
        var result2 = await _context.Products.Cached("fast-expire").ToListAsync();
        result2.Should().BeEmpty("profile TTL should have expired");
    }

    #endregion

    #region CacheAllQueries mode

    [Fact]
    public async Task CacheAllQueries_CachesQueriesWithoutStashTag()
    {
        _options.CacheAllQueries = true;
        SeedProducts("Auto");

        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1, "CacheAllQueries should cache even without .Cached()");
    }

    #endregion

    #region ExcludedTables filtering

    [Fact]
    public async Task ExcludedTables_SkipsCachingForExcludedTable()
    {
        _options.CacheAllQueries = true;
        _options.ExcludedTables.Add("Products");
        SeedProducts("Excluded");

        await _context.Products.ToListAsync();
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result = await _context.Products.ToListAsync();
        result.Should().BeEmpty("Products table is in ExcludedTables");
    }

    [Fact]
    public async Task ExcludedTables_DoesNotAffectExplicitStashTag()
    {
        _options.CacheAllQueries = true;
        _options.ExcludedTables.Add("Products");
        SeedProducts("ForceCached");

        // Explicit .Cached() should override ExcludedTables
        var result1 = await _context.Products.Cached().ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.Cached().ToListAsync();
        result2.Should().HaveCount(1, "explicit .Cached() should override ExcludedTables");
    }

    [Fact]
    public async Task ExcludedTables_NonExcludedTableStillCached()
    {
        _options.CacheAllQueries = true;
        _options.ExcludedTables.Add("Products");

        _context.Orders.Add(new TestOrder { Description = "Order1" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var result1 = await _context.Orders.ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Orders");

        var result2 = await _context.Orders.ToListAsync();
        result2.Should().HaveCount(1, "Orders table is not excluded");
    }

    #endregion

    #region MaxRowsPerQuery enforcement

    [Fact]
    public async Task MaxRowsPerQuery_LargeResultSetNotCached()
    {
        _options.CacheAllQueries = true;
        _options.MaxRowsPerQuery = 2;

        // Insert 5 rows
        for (var i = 0; i < 5; i++)
            _context.Products.Add(new TestProduct { Name = $"P{i}" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        // First query — 5 rows exceeds MaxRowsPerQuery=2, not cached
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(5);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        // Should hit empty DB because result wasn't cached
        var result2 = await _context.Products.ToListAsync();
        result2.Should().BeEmpty("result exceeded MaxRowsPerQuery and should not have been cached");
    }

    [Fact]
    public async Task MaxRowsPerQuery_SmallResultSetIsCached()
    {
        _options.CacheAllQueries = true;
        _options.MaxRowsPerQuery = 10;
        SeedProducts("Small");

        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1, "result within MaxRowsPerQuery should be cached");
    }

    #endregion

    #region MaxCacheEntrySize enforcement

    [Fact]
    public async Task MaxCacheEntrySize_OversizedResultNotCached()
    {
        _options.CacheAllQueries = true;
        _options.MaxCacheEntrySize = 1; // 1 byte — everything will be oversized
        SeedProducts("Big");

        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.ToListAsync();
        result2.Should().BeEmpty("result exceeded MaxCacheEntrySize");
    }

    [Fact]
    public async Task MaxCacheEntrySize_Zero_DisablesSizeLimit()
    {
        _options.CacheAllQueries = true;
        _options.MaxCacheEntrySize = 0; // Disabled (default)
        SeedProducts("Normal");

        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1, "MaxCacheEntrySize=0 means no size limit");
    }

    #endregion

    #region FallbackToDatabase

    [Fact]
    public async Task FallbackToDatabase_CacheThrows_QueryStillSucceeds()
    {
        var failingStore = new FailingCacheStore();
        var options = new StashOptions { CacheAllQueries = true, FallbackToDatabase = true };
        var keyGen = new DefaultCacheKeyGenerator(options);
        var interceptor = new StashCommandInterceptor(
            failingStore, keyGen, options, NullLogger<StashCommandInterceptor>.Instance);

        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var contextOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(interceptor)
            .Options;
        using var context = new TestDbContext(contextOptions);
        context.Database.EnsureCreated();
        context.Products.Add(new TestProduct { Name = "Fallback" });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Should succeed despite cache store throwing
        var result = await context.Products.ToListAsync();
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Fallback");
    }

    [Fact]
    public async Task FallbackToDatabase_False_CacheThrows_ExceptionPropagates()
    {
        var failingStore = new FailingCacheStore();
        var options = new StashOptions { FallbackToDatabase = false };
        var keyGen = new DefaultCacheKeyGenerator(options);
        var interceptor = new StashCommandInterceptor(
            failingStore, keyGen, options, NullLogger<StashCommandInterceptor>.Instance);

        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        // Create DB and seed without the failing interceptor
        var setupOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(conn)
            .Options;
        using (var setupContext = new TestDbContext(setupOptions))
        {
            setupContext.Database.EnsureCreated();
            setupContext.Products.Add(new TestProduct { Name = "NoCatch" });
            setupContext.SaveChanges();
        }

        // Now use the failing interceptor with CacheAllQueries
        options.CacheAllQueries = true;
        var contextOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(interceptor)
            .Options;
        using var context = new TestDbContext(contextOptions);

        var act = () => context.Products.ToListAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cache unavailable");
    }

    #endregion

    #region StashNoCache

    [Fact]
    public async Task StashNoCache_SkipsCaching()
    {
        _options.CacheAllQueries = true;
        SeedProducts("NoCacheMe");

        // Query with NoCache — should not be cached
        var result1 = await _context.Products.NoStash().ToListAsync();
        result1.Should().HaveCount(1);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var result2 = await _context.Products.NoStash().ToListAsync();
        result2.Should().BeEmpty("StashNoCache should prevent caching");
    }

    #endregion

    #region Scalar caching

    [Fact]
    public async Task ScalarQuery_CountAsync_CachedOnSecondCall()
    {
        _options.CacheAllQueries = true;
        SeedProducts("A", "B", "C");

        var count1 = await _context.Products.CountAsync();
        count1.Should().Be(3);

        await _context.Database.ExecuteSqlRawAsync("DELETE FROM Products");

        var count2 = await _context.Products.CountAsync();
        count2.Should().Be(3, "scalar result should come from cache");
    }

    #endregion

    #region Cache invalidation via SaveChanges

    [Fact]
    public async Task SaveChanges_InvalidatesRelatedCacheEntries()
    {
        _options.CacheAllQueries = true;
        SeedProducts("Original");

        // Populate cache
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        // Modify via EF (triggers SaveChanges invalidation)
        _context.Products.Add(new TestProduct { Name = "New" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache should be invalidated — fresh DB query
        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(2);
    }

    #endregion

    #region Concurrent queries

    [Fact]
    public async Task ConcurrentQueries_AllReturnCorrectResults()
    {
        _options.CacheAllQueries = true;
        SeedProducts("Concurrent1", "Concurrent2");

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            // Each iteration creates its own DbContext on the same connection
            // to avoid concurrency issues with a single context
            var contextOptions = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(_interceptor)
                .Options;
            await using var ctx = new TestDbContext(contextOptions);
            var results = await ctx.Products.ToListAsync();
            return results.Count;
        });

        var counts = await Task.WhenAll(tasks);
        counts.Should().AllBeEquivalentTo(2);
    }

    #endregion

    #region ShouldCache unit tests

    [Fact]
    public void ShouldCache_NonSelectCommand_ReturnsFalse()
    {
        var command = new FakeDbCommand("INSERT INTO Products (Name) VALUES ('Test')");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeFalse();
    }

    [Fact]
    public void ShouldCache_AlreadySuppressed_ReturnsFalse()
    {
        var command = new FakeDbCommand("SELECT * FROM Products");
        _options.CacheAllQueries = true;
        _interceptor.ShouldCache(command, hasResult: true).Should().BeFalse();
    }

    [Fact]
    public void ShouldCache_SelectWithStashTag_ReturnsTrue()
    {
        var command = new FakeDbCommand("-- Stash:TTL=0\nSELECT * FROM Products");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeTrue();
    }

    [Fact]
    public void ShouldCache_SelectWithNoCacheTag_ReturnsFalse()
    {
        _options.CacheAllQueries = true;
        var command = new FakeDbCommand("-- Stash:NoCache\nSELECT * FROM Products");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeFalse();
    }

    [Fact]
    public void ShouldCache_CacheAllQueries_SelectWithoutTag_ReturnsTrue()
    {
        _options.CacheAllQueries = true;
        var command = new FakeDbCommand("SELECT * FROM Products");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeTrue();
    }

    [Fact]
    public void ShouldCache_CacheAllQueriesFalse_SelectWithoutTag_ReturnsFalse()
    {
        _options.CacheAllQueries = false;
        var command = new FakeDbCommand("SELECT * FROM Products");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeFalse();
    }

    [Fact]
    public void ShouldCache_WithCte_ReturnsTrue()
    {
        _options.CacheAllQueries = true;
        var command = new FakeDbCommand("WITH cte AS (SELECT * FROM Products) SELECT * FROM cte");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeTrue();
    }

    [Fact]
    public void ShouldCache_UpdateCommand_ReturnsFalse()
    {
        _options.CacheAllQueries = true;
        var command = new FakeDbCommand("UPDATE Products SET Name = 'X' WHERE Id = 1");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeFalse();
    }

    [Fact]
    public void ShouldCache_DeleteCommand_ReturnsFalse()
    {
        _options.CacheAllQueries = true;
        var command = new FakeDbCommand("DELETE FROM Products WHERE Id = 1");
        _interceptor.ShouldCache(command, hasResult: false).Should().BeFalse();
    }

    #endregion

    #region IsReadCommand unit tests

    [Theory]
    [InlineData("SELECT * FROM Products", true)]
    [InlineData("WITH cte AS (SELECT 1) SELECT * FROM cte", true)]
    [InlineData("-- comment\nSELECT * FROM Products", true)]
    [InlineData("/* block */\nSELECT * FROM Products", true)]
    [InlineData("-- Stash:TTL=0,Sliding=0,Profile=\nSELECT * FROM Products", true)]
    [InlineData("INSERT INTO Products (Name) VALUES ('X')", false)]
    [InlineData("UPDATE Products SET Name = 'X'", false)]
    [InlineData("DELETE FROM Products", false)]
    [InlineData("MERGE INTO Products USING ...", false)]
    [InlineData("", false)]
    public void IsReadCommand_DetectsCorrectly(string sql, bool expected)
    {
        StashCommandInterceptor.IsReadCommand(sql).Should().Be(expected);
    }

    #endregion

    #region ResolveTtl unit tests

    [Fact]
    public void ResolveTtl_NoTag_ReturnsDefaults()
    {
        var command = new FakeDbCommand("SELECT * FROM Products");
        var (abs, sliding) = _interceptor.ResolveTtl(command);
        abs.Should().Be(_options.DefaultAbsoluteExpiration);
        sliding.Should().Be(_options.DefaultSlidingExpiration);
    }

    [Fact]
    public void ResolveTtl_WithInlineTtl_ParsesCorrectly()
    {
        var command = new FakeDbCommand("-- Stash:TTL=60,Sliding=10\nSELECT 1");
        var (abs, sliding) = _interceptor.ResolveTtl(command);
        abs.Should().Be(TimeSpan.FromSeconds(60));
        sliding.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ResolveTtl_WithZeroTtl_ReturnsDefaults()
    {
        var command = new FakeDbCommand("-- Stash:TTL=0\nSELECT 1");
        var (abs, sliding) = _interceptor.ResolveTtl(command);
        abs.Should().Be(_options.DefaultAbsoluteExpiration);
        sliding.Should().Be(_options.DefaultSlidingExpiration);
    }

    [Fact]
    public void ResolveTtl_WithProfile_UsesProfileSettings()
    {
        _options.Profiles["hot"] = new StashProfile
        {
            Name = "hot",
            AbsoluteExpiration = TimeSpan.FromSeconds(5),
            SlidingExpiration = TimeSpan.FromSeconds(2)
        };

        var command = new FakeDbCommand("-- Stash:Profile=hot\nSELECT 1");
        var (abs, sliding) = _interceptor.ResolveTtl(command);
        abs.Should().Be(TimeSpan.FromSeconds(5));
        sliding.Should().Be(TimeSpan.FromSeconds(2));
    }

    #endregion

    /// <summary>
    /// Fake clock for controlling MemoryCache time in tests.
    /// </summary>
#pragma warning disable CS0618 // ISystemClock is obsolete
    private sealed class FakeSystemClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public DateTimeOffset UtcNow => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
#pragma warning restore CS0618
}
