using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Benchmarks.Infrastructure;
using Stash.EFCore.Extensions;

namespace Stash.EFCore.Benchmarks;

/// <summary>
/// Compares cold query (cache miss, hits SQLite) vs warm query (cache hit, served from memory).
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class CacheHitVsMissBenchmarks
{
    private BenchmarkFixture _fixture = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture(productCount: 100);
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [Benchmark(Baseline = true, Description = "Cold (cache miss → SQLite)")]
    public async Task<int> CacheMiss()
    {
        // Invalidate to force a miss every iteration
        await _fixture.ClearCacheAsync();

        using var ctx = _fixture.CreateCachedContext();
        var products = await ctx.Products
            .Where(p => p.IsActive)
            .Cached()
            .ToListAsync();
        return products.Count;
    }

    [Benchmark(Description = "Warm (cache hit → memory)")]
    public async Task<int> CacheHit()
    {
        // Ensure cache is warm — first call fills it, second reads from cache.
        // We do the warm-up once per invocation batch in IterationSetup,
        // but BDN calls this benchmark method many times. The first invocation
        // warms the cache; all subsequent ones are hits.
        using var ctx = _fixture.CreateCachedContext();
        var products = await ctx.Products
            .Where(p => p.IsActive)
            .Cached()
            .ToListAsync();
        return products.Count;
    }

    [IterationSetup(Target = nameof(CacheHit))]
    public void WarmCache()
    {
        // Ensure the cache is populated before the iteration
        using var ctx = _fixture.CreateCachedContext();
        ctx.Products.Where(p => p.IsActive).Cached().ToListAsync().GetAwaiter().GetResult();
    }
}
