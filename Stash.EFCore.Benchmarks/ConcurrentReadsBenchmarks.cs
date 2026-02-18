using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Benchmarks.Infrastructure;
using Stash.EFCore.Extensions;

namespace Stash.EFCore.Benchmarks;

/// <summary>
/// Benchmarks cache throughput under concurrent readers hitting the same cached query.
/// Measures total time and per-reader allocation.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class ConcurrentReadsBenchmarks
{
    private BenchmarkFixture _fixture = null!;

    [Params(1, 10, 50, 100)]
    public int ConcurrentReaders { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture(productCount: 100);

        // Warm the cache once so all benchmark iterations are cache hits
        using var ctx = _fixture.CreateCachedContext();
        ctx.Products.Where(p => p.IsActive).Cached().ToListAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [Benchmark(Description = "Concurrent cached reads")]
    public async Task<int> ConcurrentCachedReads()
    {
        var tasks = new Task<int>[ConcurrentReaders];
        for (var i = 0; i < ConcurrentReaders; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                using var ctx = _fixture.CreateCachedContext();
                var products = await ctx.Products
                    .Where(p => p.IsActive)
                    .Cached()
                    .ToListAsync();
                return products.Count;
            });
        }

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }
}
