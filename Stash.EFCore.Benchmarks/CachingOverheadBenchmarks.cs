using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Benchmarks.Infrastructure;
using Stash.EFCore.Extensions;

namespace Stash.EFCore.Benchmarks;

/// <summary>
/// Measures the overhead Stash.EFCore adds to a cache-miss query
/// compared to a baseline query with no interceptors at all.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class CachingOverheadBenchmarks
{
    private BenchmarkFixture _fixture = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture(productCount: 100);
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [Benchmark(Baseline = true, Description = "Baseline (no interceptors)")]
    public async Task<int> BaselineNoCache()
    {
        using var ctx = _fixture.CreateBaselineContext();
        var products = await ctx.Products
            .Where(p => p.IsActive)
            .ToListAsync();
        return products.Count;
    }

    [Benchmark(Description = "Stash cache miss (intercept + capture + store)")]
    public async Task<int> StashCacheMiss()
    {
        // Clear cache before each invocation to ensure a miss
        await _fixture.ClearCacheAsync();

        using var ctx = _fixture.CreateCachedContext();
        var products = await ctx.Products
            .Where(p => p.IsActive)
            .Cached()
            .ToListAsync();
        return products.Count;
    }
}
