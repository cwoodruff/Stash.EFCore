using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Interceptors;
using Xunit;

namespace Stash.EFCore.Tests.Integration;

public class EdgeCaseTests : IDisposable
{
    private readonly CacheTestFixture _f;

    public EdgeCaseTests()
    {
        _f = new CacheTestFixture();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task EmptyResultSet_IsCached()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products
            .Where(p => p.Name == "NonexistentProduct")
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);
        result1.Should().BeEmpty();

        // Second query — should come from cache
        _f.SqlCounter.Reset();
        var result2 = await ctx.Products
            .Where(p => p.Name == "NonexistentProduct")
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "empty result set should be cached");
        result2.Should().BeEmpty();
    }

    [Fact]
    public async Task NullScalar_FirstOrDefaultAsync_CachedCorrectly()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products
            .Where(p => p.Name == "NonexistentProduct")
            .Cached()
            .FirstOrDefaultAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);
        result1.Should().BeNull();

        _f.SqlCounter.Reset();
        var result2 = await ctx.Products
            .Where(p => p.Name == "NonexistentProduct")
            .Cached()
            .FirstOrDefaultAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "null result should be cached");
        result2.Should().BeNull();
    }

    [Fact]
    public async Task LargeResult_ExceedingMaxRows_NotCached()
    {
        // Create a fixture with very low MaxRowsPerQuery
        var f = new CacheTestFixture(opts => opts.MaxRowsPerQuery = 5);

        using var ctx = f.CreateContext();

        // Query returning 50 rows exceeds the 5-row limit
        f.SqlCounter.Reset();
        var result1 = await ctx.Products.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);
        result1.Should().HaveCount(50);

        // Second query should also hit DB (not cached due to row limit)
        f.SqlCounter.Reset();
        var result2 = await ctx.Products.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "result exceeding MaxRowsPerQuery should not be cached");
        result2.Should().HaveCount(50);

        // But a small query should still be cached
        f.SqlCounter.Reset();
        var small1 = await ctx.Products.Where(p => p.Id <= 3).Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        var small2 = await ctx.Products.Where(p => p.Id <= 3).Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "small result under MaxRowsPerQuery should be cached");

        f.Dispose();
    }

    [Fact]
    public async Task AllColumnTypes_CachedAndReplayedAccurately()
    {
        using var ctx = _f.CreateContext();

        // Insert an entity with various column types
        var entity = new AllTypesEntity
        {
            StringVal = "Hello World",
            IntVal = 42,
            LongVal = 9_876_543_210L,
            DoubleVal = 3.14159,
            DecimalVal = 99.99m,
            BoolVal = true,
            DateTimeVal = new DateTime(2025, 6, 15, 12, 30, 0),
            BlobVal = [0x01, 0x02, 0x03, 0xFF],
            NullableStringVal = null
        };
        ctx.AllTypes.Add(entity);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Cache the query
        _f.SqlCounter.Reset();
        var result1 = await ctx.AllTypes.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        // Read from cache
        _f.SqlCounter.Reset();
        var result2 = await ctx.AllTypes.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "should be from cache");

        // Verify all values round-trip correctly
        var cached = result2.Single();
        cached.StringVal.Should().Be("Hello World");
        cached.IntVal.Should().Be(42);
        cached.LongVal.Should().Be(9_876_543_210L);
        cached.DoubleVal.Should().BeApproximately(3.14159, 0.00001);
        cached.DecimalVal.Should().Be(99.99m);
        cached.BoolVal.Should().BeTrue();
        cached.DateTimeVal.Should().Be(new DateTime(2025, 6, 15, 12, 30, 0));
        cached.BlobVal.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, 0xFF });
        cached.NullableStringVal.Should().BeNull();
    }

    [Fact]
    public async Task MultipleDbContextInstances_ShareCache()
    {
        // First context caches the query
        using (var ctx1 = _f.CreateContext())
        {
            _f.SqlCounter.Reset();
            var result1 = await ctx1.Products.Cached().ToListAsync();
            _f.SqlCounter.ExecutionCount.Should().Be(1);
            result1.Should().HaveCount(50);
        }

        // Second context instance should get cache hit
        using (var ctx2 = _f.CreateContext())
        {
            _f.SqlCounter.Reset();
            var result2 = await ctx2.Products.Cached().ToListAsync();
            _f.SqlCounter.ExecutionCount.Should().Be(0,
                "second DbContext instance should share the same cache");
            result2.Should().HaveCount(50);
        }
    }

    [Fact]
    public async Task MaxCacheEntrySize_LargeEntry_NotCached()
    {
        // Create fixture with very small max entry size
        var f = new CacheTestFixture(opts => opts.MaxCacheEntrySize = 100);
        using var ctx = f.CreateContext();

        // Query returning 50 products will exceed 100 bytes
        f.SqlCounter.Reset();
        await ctx.Products.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        f.SqlCounter.Reset();
        await ctx.Products.Cached().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "entry exceeding MaxCacheEntrySize should not be cached");

        f.Dispose();
    }

    [Fact]
    public async Task QueryWithGroupBy_CachedCorrectly()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);
        result1.Should().HaveCount(5);
        result1.Should().AllSatisfy(g => g.Count.Should().Be(10));

        _f.SqlCounter.Reset();
        var result2 = await ctx.Products
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "GroupBy result should be cached");
        result2.Should().HaveCount(5);
    }
}
