using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Extensions;
using Xunit;

namespace Stash.EFCore.Tests.Integration;

public class BasicCachingTests : IDisposable
{
    private readonly CacheTestFixture _f;

    public BasicCachingTests()
    {
        _f = new CacheTestFixture();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task FirstQuery_HitsDb_SecondQuery_ServesFromCache()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products.Cached().ToListAsync();
        var dbHits1 = _f.SqlCounter.ExecutionCount;

        _f.SqlCounter.Reset();
        var result2 = await ctx.Products.Cached().ToListAsync();
        var dbHits2 = _f.SqlCounter.ExecutionCount;

        result1.Should().HaveCount(50);
        result2.Should().HaveCount(50);
        dbHits1.Should().Be(1, "first query should hit DB");
        dbHits2.Should().Be(0, "second identical query should serve from cache");
    }

    [Fact]
    public async Task DifferentParameters_ProduceDifferentCacheEntries()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var cat1 = await ctx.Products.Where(p => p.CategoryId == 1).Cached().ToListAsync();
        var cat2 = await ctx.Products.Where(p => p.CategoryId == 2).Cached().ToListAsync();

        _f.SqlCounter.ExecutionCount.Should().Be(2, "different parameters should produce different cache entries");
        cat1.Should().HaveCount(10);
        cat2.Should().HaveCount(10);

        // Now both should be cached
        _f.SqlCounter.Reset();
        await ctx.Products.Where(p => p.CategoryId == 1).Cached().ToListAsync();
        await ctx.Products.Where(p => p.CategoryId == 2).Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "both should come from cache");
    }

    [Fact]
    public async Task ExplicitTtl_OverridesDefault()
    {
        using var ctx = _f.CreateContext();

        // Query with 5-second TTL — should cache
        var result1 = await ctx.Products.Cached(TimeSpan.FromSeconds(5)).ToListAsync();
        result1.Should().HaveCount(50);

        _f.SqlCounter.Reset();
        var result2 = await ctx.Products.Cached(TimeSpan.FromSeconds(5)).ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "should serve from cache");
    }

    [Fact]
    public async Task NoStash_BypassesCacheEvenWhenCacheAllQueries()
    {
        var f = new CacheTestFixture(opts => opts.CacheAllQueries = true);
        using var ctx = f.CreateContext();

        // First query without .NoStash() — should be auto-cached
        await ctx.Products.ToListAsync();

        f.SqlCounter.Reset();
        await ctx.Products.ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0, "CacheAllQueries should cache");

        // Same query with .NoStash() — should hit DB every time
        f.SqlCounter.Reset();
        await ctx.Products.NoStash().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "NoStash should bypass cache");

        f.SqlCounter.Reset();
        await ctx.Products.NoStash().ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "NoStash should always hit DB");

        f.Dispose();
    }

    [Fact]
    public async Task ScalarCountAsync_IsCached()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var count1 = await ctx.Products.Cached().CountAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        var count2 = await ctx.Products.Cached().CountAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "scalar count should be cached");

        count1.Should().Be(50);
        count2.Should().Be(50);
    }

    [Fact]
    public async Task ScalarAnyAsync_IsCached()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var any1 = await ctx.Products.Where(p => p.Name == "Product-1").Cached().AnyAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        var any2 = await ctx.Products.Where(p => p.Name == "Product-1").Cached().AnyAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "scalar any should be cached");

        any1.Should().BeTrue();
        any2.Should().BeTrue();
    }

    [Fact]
    public async Task ScalarSumAsync_IsCached()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var sum1 = await ctx.Products.Where(p => p.CategoryId == 1).Cached().SumAsync(p => p.Price);
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        var sum2 = await ctx.Products.Where(p => p.CategoryId == 1).Cached().SumAsync(p => p.Price);
        _f.SqlCounter.ExecutionCount.Should().Be(0, "scalar sum should be cached");

        sum1.Should().Be(sum2);
    }

    [Fact]
    public async Task IncludeQuery_CachedAndReplayedCorrectly()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products
            .Include(p => p.Category)
            .Where(p => p.CategoryId == 1)
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        result1.Should().HaveCount(10);
        result1.Should().AllSatisfy(p => p.Category.Should().NotBeNull());
        result1[0].Category.Name.Should().Be("Electronics");

        // Second query from cache
        _f.SqlCounter.Reset();
        var result2 = await ctx.Products
            .Include(p => p.Category)
            .Where(p => p.CategoryId == 1)
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0);

        result2.Should().HaveCount(10);
        result2.Should().AllSatisfy(p => p.Category.Should().NotBeNull());
    }

    [Fact]
    public async Task SelectProjection_CachedCorrectly()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var result1 = await ctx.Products
            .Where(p => p.IsActive)
            .Select(p => new { p.Name, p.Price })
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        var result2 = await ctx.Products
            .Where(p => p.IsActive)
            .Select(p => new { p.Name, p.Price })
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0);

        result1.Should().HaveCount(result2.Count);
        result1.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task OrderBySkipTake_CachedCorrectly()
    {
        using var ctx = _f.CreateContext();

        _f.SqlCounter.Reset();
        var page1 = await ctx.Products
            .OrderBy(p => p.Id)
            .Skip(10)
            .Take(5)
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        _f.SqlCounter.Reset();
        var page1Again = await ctx.Products
            .OrderBy(p => p.Id)
            .Skip(10)
            .Take(5)
            .Cached()
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0);

        page1.Should().HaveCount(5);
        page1.Select(p => p.Id).Should().BeEquivalentTo(page1Again.Select(p => p.Id));
    }
}
