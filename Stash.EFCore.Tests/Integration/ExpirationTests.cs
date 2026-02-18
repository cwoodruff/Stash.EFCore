using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Xunit;

namespace Stash.EFCore.Tests.Integration;

public class ExpirationTests : IDisposable
{
    private readonly CacheTestFixture _f;

    public ExpirationTests()
    {
        _f = new CacheTestFixture();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task AbsoluteTtl_ExpiredEntry_CausesDbHit()
    {
        using var ctx = _f.CreateContext();

        // Cache with 1-second TTL
        _f.SqlCounter.Reset();
        var result1 = await ctx.Products
            .Where(p => p.CategoryId == 1)
            .Cached(TimeSpan.FromSeconds(1))
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        // Immediate re-query — should be cached
        _f.SqlCounter.Reset();
        await ctx.Products
            .Where(p => p.CategoryId == 1)
            .Cached(TimeSpan.FromSeconds(1))
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "should still be cached");

        // Wait for TTL to expire
        await Task.Delay(1500);

        // Re-query — should hit DB
        _f.SqlCounter.Reset();
        var result2 = await ctx.Products
            .Where(p => p.CategoryId == 1)
            .Cached(TimeSpan.FromSeconds(1))
            .ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1, "TTL expired, should hit DB");
        result2.Should().HaveCount(result1.Count);
    }

    [Fact]
    public async Task CacheProfileTtl_OverridesDefaultExpiration()
    {
        var f = new CacheTestFixture(opts =>
        {
            opts.DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30);
            opts.Profiles["short-lived"] = new StashProfile
            {
                Name = "short-lived",
                AbsoluteExpiration = TimeSpan.FromSeconds(1)
            };
        });

        using var ctx = f.CreateContext();

        // Cache with short-lived profile
        f.SqlCounter.Reset();
        await ctx.Products.Cached("short-lived").ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1);

        // Immediate re-query — cached
        f.SqlCounter.Reset();
        await ctx.Products.Cached("short-lived").ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(0);

        // Wait for profile TTL to expire
        await Task.Delay(1500);

        // Should hit DB again
        f.SqlCounter.Reset();
        await ctx.Products.Cached("short-lived").ToListAsync();
        f.SqlCounter.ExecutionCount.Should().Be(1, "profile TTL expired, should hit DB");

        f.Dispose();
    }

    [Fact]
    public async Task DefaultTtl_QueriesRemainsInCache()
    {
        using var ctx = _f.CreateContext();

        // Cache with default TTL (30 min) — should survive short delays
        _f.SqlCounter.Reset();
        await ctx.Categories.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1);

        await Task.Delay(100);

        _f.SqlCounter.Reset();
        await ctx.Categories.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "default 30min TTL should not expire in 100ms");
    }
}
