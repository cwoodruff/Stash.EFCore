using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Diagnostics;
using Stash.EFCore.Extensions;
using Stash.EFCore.Interceptors;
using Xunit;

namespace Stash.EFCore.Tests;

#region Test entities

public class EvtProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class EvtDbContext : DbContext
{
    public DbSet<EvtProduct> Products => Set<EvtProduct>();

    public EvtDbContext(DbContextOptions<EvtDbContext> options) : base(options) { }
}

#endregion

public class StashEventCallbackTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StashOptions _options;
    private readonly StashStatistics _statistics;
    private readonly EvtDbContext _context;
    private readonly List<StashEvent> _events = [];

    public StashEventCallbackTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _statistics = new StashStatistics();
        _options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30),
            CacheAllQueries = true,
            OnStashEvent = e => _events.Add(e)
        };

        var keyGen = new DefaultCacheKeyGenerator(_options);
        var cacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
        var commandInterceptor = new StashCommandInterceptor(
            cacheStore, keyGen, _options, NullLogger<StashCommandInterceptor>.Instance, _statistics);
        var invalidationInterceptor = new StashInvalidationInterceptor(
            cacheStore, NullLogger<StashInvalidationInterceptor>.Instance, _options, _statistics);

        var contextOptions = new DbContextOptionsBuilder<EvtDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(commandInterceptor, invalidationInterceptor)
            .Options;

        _context = new EvtDbContext(contextOptions);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CacheMiss_FiresEvent_WithCorrectType()
    {
        _context.Products.Add(new EvtProduct { Name = "Test", Price = 9.99m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        _events.Clear();

        await _context.Products.ToListAsync();

        _events.Should().Contain(e => e.EventType == StashEventType.CacheMiss);
    }

    [Fact]
    public async Task CacheHit_FiresEvent_WithDuration()
    {
        _context.Products.Add(new EvtProduct { Name = "Test", Price = 9.99m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // First query — cache miss
        await _context.Products.ToListAsync();
        _events.Clear();

        // Second query — cache hit
        await _context.Products.ToListAsync();

        var hitEvent = _events.FirstOrDefault(e => e.EventType == StashEventType.CacheHit);
        hitEvent.Should().NotBeNull();
        hitEvent!.CacheKey.Should().NotBeNullOrEmpty();
        hitEvent.Duration.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryResultCached_FiresEvent_WithRowCountAndSize()
    {
        _context.Products.Add(new EvtProduct { Name = "Test", Price = 9.99m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        _events.Clear();

        await _context.Products.ToListAsync();

        var cachedEvent = _events.FirstOrDefault(e => e.EventType == StashEventType.QueryResultCached);
        cachedEvent.Should().NotBeNull();
        cachedEvent!.RowCount.Should().BeGreaterThanOrEqualTo(1);
        cachedEvent.SizeBytes.Should().BeGreaterThan(0);
        cachedEvent.Ttl.Should().Be(_options.DefaultAbsoluteExpiration);
        cachedEvent.Tables.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CacheInvalidated_FiresEvent_WithTableNames()
    {
        _context.Products.Add(new EvtProduct { Name = "Test", Price = 9.99m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        _events.Clear();

        // Trigger invalidation by saving a change
        _context.Products.Add(new EvtProduct { Name = "Test2", Price = 19.99m });
        await _context.SaveChangesAsync();

        var invalidatedEvent = _events.FirstOrDefault(e => e.EventType == StashEventType.CacheInvalidated);
        invalidatedEvent.Should().NotBeNull();
        invalidatedEvent!.Tables.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Statistics_RecordedCorrectly_AcrossOperations()
    {
        _context.Products.Add(new EvtProduct { Name = "Test", Price = 9.99m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _statistics.Reset();

        // Cache miss
        await _context.Products.ToListAsync();
        _statistics.Misses.Should().Be(1);

        // Cache hit
        await _context.Products.ToListAsync();
        _statistics.Hits.Should().Be(1);

        _statistics.TotalBytesCached.Should().BeGreaterThan(0);
        _statistics.HitRate.Should().Be(50.0);
    }

    [Fact]
    public async Task Statistics_InvalidationCount_Tracked()
    {
        _context.Products.Add(new EvtProduct { Name = "Test", Price = 9.99m });
        await _context.SaveChangesAsync();

        _statistics.Reset();

        // Trigger invalidation
        var product = await _context.Products.FirstAsync();
        product.Name = "Updated";
        await _context.SaveChangesAsync();

        _statistics.Invalidations.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SkippedTooManyRows_FiresEvent()
    {
        var options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30),
            CacheAllQueries = true,
            MaxRowsPerQuery = 1,
            OnStashEvent = e => _events.Add(e)
        };

        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var keyGen = new DefaultCacheKeyGenerator(options);
        var cacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
        var commandInterceptor = new StashCommandInterceptor(
            cacheStore, keyGen, options, NullLogger<StashCommandInterceptor>.Instance);
        var invalidationInterceptor = new StashInvalidationInterceptor(
            cacheStore, NullLogger<StashInvalidationInterceptor>.Instance, options);

        var contextOptions = new DbContextOptionsBuilder<EvtDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandInterceptor, invalidationInterceptor)
            .Options;

        using var ctx = new EvtDbContext(contextOptions);
        ctx.Database.EnsureCreated();

        ctx.Products.AddRange(
            new EvtProduct { Name = "P1", Price = 1m },
            new EvtProduct { Name = "P2", Price = 2m });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        _events.Clear();

        await ctx.Products.ToListAsync();

        _events.Should().Contain(e => e.EventType == StashEventType.SkippedTooManyRows);

        connection.Dispose();
    }
}
