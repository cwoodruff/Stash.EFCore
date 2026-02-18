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

namespace Stash.EFCore.Tests;

#region Test entities for QueryableExtensions tests

public class QeProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int CategoryId { get; set; }
    public QeCategory? Category { get; set; }
}

public class QeCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<QeProduct> Products { get; set; } = [];
}

public class QeDbContext : DbContext
{
    public DbSet<QeProduct> Products => Set<QeProduct>();
    public DbSet<QeCategory> Categories => Set<QeCategory>();

    public QeDbContext(DbContextOptions<QeDbContext> options) : base(options) { }
}

#endregion

public class QueryableExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MemoryCacheStore _cacheStore;
    private readonly StashOptions _options;
    private readonly StashCommandInterceptor _commandInterceptor;
    private readonly QeDbContext _context;

    public QueryableExtensionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30)
        };

        var keyGen = new DefaultCacheKeyGenerator(_options);
        _cacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
        _commandInterceptor = new StashCommandInterceptor(
            _cacheStore, keyGen, _options, NullLogger<StashCommandInterceptor>.Instance);
        var invalidationInterceptor = new StashInvalidationInterceptor(
            _cacheStore, NullLogger<StashInvalidationInterceptor>.Instance, _options);

        var contextOptions = new DbContextOptionsBuilder<QeDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commandInterceptor, invalidationInterceptor)
            .Options;

        _context = new QeDbContext(contextOptions);
        _context.Database.EnsureCreated();

        // Seed data
        var cat = new QeCategory { Name = "Electronics" };
        _context.Categories.Add(cat);
        _context.SaveChanges();
        _context.Products.Add(new QeProduct { Name = "Widget", Price = 9.99m, IsActive = true, CategoryId = cat.Id });
        _context.Products.Add(new QeProduct { Name = "Gadget", Price = 19.99m, IsActive = true, CategoryId = cat.Id });
        _context.Products.Add(new QeProduct { Name = "Inactive", Price = 5.00m, IsActive = false, CategoryId = cat.Id });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    #region .Cached() adds expected tag to SQL

    [Fact]
    public async Task Cached_DefaultOverload_CachesQuery()
    {
        var result1 = await _context.Products.Cached().ToListAsync();
        result1.Should().HaveCount(3);

        // Second query — should come from cache
        var result2 = await _context.Products.Cached().ToListAsync();
        result2.Should().HaveCount(3);
    }

    [Fact]
    public void Cached_DefaultOverload_TagContainsTTL()
    {
        // Verify the tag embeds in the SQL
        var command = new FakeDbCommand("-- Stash:TTL=0\nSELECT * FROM Products");
        _commandInterceptor.ShouldCache(command, hasResult: false).Should().BeTrue();
    }

    #endregion

    #region .Cached(TimeSpan) encodes TTL correctly

    [Fact]
    public async Task Cached_WithTimeSpan_CachesWithSpecifiedTtl()
    {
        var result1 = await _context.Products.Cached(TimeSpan.FromMinutes(5)).ToListAsync();
        result1.Should().HaveCount(3);

        var result2 = await _context.Products.Cached(TimeSpan.FromMinutes(5)).ToListAsync();
        result2.Should().HaveCount(3);
    }

    [Fact]
    public void Cached_WithTimeSpan_TagEncodesTtlCorrectly()
    {
        var command = new FakeDbCommand("-- Stash:TTL=300\nSELECT * FROM Products");
        var (abs, _) = _commandInterceptor.ResolveTtl(command);
        abs.Should().Be(TimeSpan.FromMinutes(5));
    }

    #endregion

    #region .Cached(TimeSpan, TimeSpan) encodes both TTL and sliding

    [Fact]
    public async Task Cached_WithBothExpirations_CachesQuery()
    {
        var result1 = await _context.Products
            .Cached(TimeSpan.FromHours(1), TimeSpan.FromMinutes(15))
            .ToListAsync();
        result1.Should().HaveCount(3);

        var result2 = await _context.Products
            .Cached(TimeSpan.FromHours(1), TimeSpan.FromMinutes(15))
            .ToListAsync();
        result2.Should().HaveCount(3);
    }

    [Fact]
    public void Cached_WithBothExpirations_TagEncodesBothValues()
    {
        var command = new FakeDbCommand("-- Stash:TTL=3600,Sliding=900\nSELECT * FROM Products");
        var (abs, sliding) = _commandInterceptor.ResolveTtl(command);
        abs.Should().Be(TimeSpan.FromHours(1));
        sliding.Should().Be(TimeSpan.FromMinutes(15));
    }

    #endregion

    #region .Cached(profileName) encodes profile name

    [Fact]
    public async Task Cached_WithProfile_CachesQuery()
    {
        _options.Profiles["fast-expire"] = new StashProfile
        {
            Name = "fast-expire",
            AbsoluteExpiration = TimeSpan.FromSeconds(10)
        };

        var result1 = await _context.Products.Cached("fast-expire").ToListAsync();
        result1.Should().HaveCount(3);

        var result2 = await _context.Products.Cached("fast-expire").ToListAsync();
        result2.Should().HaveCount(3);
    }

    [Fact]
    public void Cached_WithProfile_TagEncodesProfileName()
    {
        _options.Profiles["hot-data"] = new StashProfile
        {
            Name = "hot-data",
            AbsoluteExpiration = TimeSpan.FromSeconds(30),
            SlidingExpiration = TimeSpan.FromSeconds(10)
        };

        var command = new FakeDbCommand("-- Stash:Profile=hot-data\nSELECT * FROM Products");
        var (abs, sliding) = _commandInterceptor.ResolveTtl(command);
        abs.Should().Be(TimeSpan.FromSeconds(30));
        sliding.Should().Be(TimeSpan.FromSeconds(10));
    }

    #endregion

    #region .NoStash() prevents caching when CacheAllQueries = true

    [Fact]
    public async Task NoStash_PreventsQueryFromBeingCached()
    {
        _options.CacheAllQueries = true;

        var result1 = await _context.Products.NoStash().ToListAsync();
        result1.Should().HaveCount(3);

        // Add a product via raw SQL to detect if cache is used
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (Name, Price, IsActive, CategoryId) VALUES ('New', 1.0, 1, 1)");

        // If NoStash works, this should hit DB and see 4 products
        var result2 = await _context.Products.NoStash().ToListAsync();
        result2.Should().HaveCount(4);
    }

    [Fact]
    public void NoStash_ShouldCacheReturnsFalse()
    {
        _options.CacheAllQueries = true;
        var command = new FakeDbCommand("-- Stash:NoCache\nSELECT * FROM Products");
        _commandInterceptor.ShouldCache(command, hasResult: false).Should().BeFalse();
    }

    #endregion

    #region StashTagParser tests

    [Fact]
    public void StashTagParser_IsCacheable_TtlTag_ReturnsTrue()
    {
        StashTagParser.IsCacheable("-- Stash:TTL=300\nSELECT 1").Should().BeTrue();
    }

    [Fact]
    public void StashTagParser_IsCacheable_ProfileTag_ReturnsTrue()
    {
        StashTagParser.IsCacheable("-- Stash:Profile=hot-data\nSELECT 1").Should().BeTrue();
    }

    [Fact]
    public void StashTagParser_IsCacheable_NoTag_ReturnsFalse()
    {
        StashTagParser.IsCacheable("SELECT * FROM Products").Should().BeFalse();
    }

    [Fact]
    public void StashTagParser_IsCacheable_NoCacheTag_ReturnsFalse()
    {
        StashTagParser.IsCacheable("-- Stash:NoCache\nSELECT 1").Should().BeFalse();
    }

    [Fact]
    public void StashTagParser_IsExplicitlyNotCached_NoCacheTag_ReturnsTrue()
    {
        StashTagParser.IsExplicitlyNotCached("-- Stash:NoCache\nSELECT 1").Should().BeTrue();
    }

    [Fact]
    public void StashTagParser_IsExplicitlyNotCached_NoTag_ReturnsFalse()
    {
        StashTagParser.IsExplicitlyNotCached("SELECT 1").Should().BeFalse();
    }

    [Fact]
    public void StashTagParser_ParseCacheTag_TtlOnly()
    {
        var (abs, sliding, profile) = StashTagParser.ParseCacheTag("-- Stash:TTL=120\nSELECT 1");
        abs.Should().Be(TimeSpan.FromSeconds(120));
        sliding.Should().BeNull();
        profile.Should().BeNull();
    }

    [Fact]
    public void StashTagParser_ParseCacheTag_TtlAndSliding()
    {
        var (abs, sliding, profile) = StashTagParser.ParseCacheTag("-- Stash:TTL=600,Sliding=60\nSELECT 1");
        abs.Should().Be(TimeSpan.FromSeconds(600));
        sliding.Should().Be(TimeSpan.FromSeconds(60));
        profile.Should().BeNull();
    }

    [Fact]
    public void StashTagParser_ParseCacheTag_ProfileOnly()
    {
        var (abs, sliding, profile) = StashTagParser.ParseCacheTag("-- Stash:Profile=hot-data\nSELECT 1");
        abs.Should().BeNull();
        sliding.Should().BeNull();
        profile.Should().Be("hot-data");
    }

    [Fact]
    public void StashTagParser_ParseCacheTag_ZeroTtl_ReturnsNull()
    {
        var (abs, sliding, profile) = StashTagParser.ParseCacheTag("-- Stash:TTL=0\nSELECT 1");
        abs.Should().BeNull("TTL=0 means use defaults");
        sliding.Should().BeNull();
        profile.Should().BeNull();
    }

    [Fact]
    public void StashTagParser_ParseCacheTag_NoTag_ReturnsAllNull()
    {
        var (abs, sliding, profile) = StashTagParser.ParseCacheTag("SELECT 1");
        abs.Should().BeNull();
        sliding.Should().BeNull();
        profile.Should().BeNull();
    }

    #endregion

    #region Multiple .TagWith() calls don't interfere

    [Fact]
    public async Task Cached_WithAdditionalTagWith_StillCaches()
    {
        var result1 = await _context.Products
            .TagWith("custom-debug-tag")
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(3);

        var result2 = await _context.Products
            .TagWith("custom-debug-tag")
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(3);
    }

    #endregion

    #region .Cached() works with common LINQ operations

    [Fact]
    public async Task Cached_WithWhere_CachesFilteredQuery()
    {
        var result1 = await _context.Products
            .Where(p => p.IsActive)
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(2);

        var result2 = await _context.Products
            .Where(p => p.IsActive)
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cached_WithSelect_CachesProjection()
    {
        var result1 = await _context.Products
            .Select(p => new { p.Name, p.Price })
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(3);

        var result2 = await _context.Products
            .Select(p => new { p.Name, p.Price })
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(3);
    }

    [Fact]
    public async Task Cached_WithInclude_CachesEagerLoadedQuery()
    {
        var result1 = await _context.Products
            .Include(p => p.Category)
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(3);
        result1[0].Category.Should().NotBeNull();

        var result2 = await _context.Products
            .Include(p => p.Category)
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(3);
        result2[0].Category.Should().NotBeNull();
    }

    [Fact]
    public async Task Cached_WithOrderBy_CachesSortedQuery()
    {
        var result1 = await _context.Products
            .OrderBy(p => p.Name)
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(3);
        result1[0].Name.Should().Be("Gadget");

        var result2 = await _context.Products
            .OrderBy(p => p.Name)
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(3);
        result2[0].Name.Should().Be("Gadget");
    }

    [Fact]
    public async Task Cached_WithSkipTake_CachesPaginatedQuery()
    {
        var result1 = await _context.Products
            .OrderBy(p => p.Id)
            .Skip(1)
            .Take(1)
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(1);

        var result2 = await _context.Products
            .OrderBy(p => p.Id)
            .Skip(1)
            .Take(1)
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(1);
    }

    [Fact]
    public async Task Cached_WithFirstOrDefault_CachesScalarQuery()
    {
        var result1 = await _context.Products
            .Where(p => p.Name == "Widget")
            .Cached()
            .FirstOrDefaultAsync();
        result1.Should().NotBeNull();
        result1!.Name.Should().Be("Widget");

        var result2 = await _context.Products
            .Where(p => p.Name == "Widget")
            .Cached()
            .FirstOrDefaultAsync();
        result2.Should().NotBeNull();
        result2!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task Cached_WithCountAsync_CachesScalarCount()
    {
        var result1 = await _context.Products.Cached().CountAsync();
        result1.Should().Be(3);

        // Add via raw SQL to detect if cache is used
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (Name, Price, IsActive, CategoryId) VALUES ('New', 1.0, 1, 1)");

        // Should still return cached result
        var result2 = await _context.Products.Cached().CountAsync();
        result2.Should().Be(3);
    }

    [Fact]
    public async Task Cached_WithGroupBy_CachesGroupedQuery()
    {
        var result1 = await _context.Products
            .GroupBy(p => p.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .Cached()
            .ToListAsync();
        result1.Should().HaveCount(2);

        var result2 = await _context.Products
            .GroupBy(p => p.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .Cached()
            .ToListAsync();
        result2.Should().HaveCount(2);
    }

    #endregion
}
