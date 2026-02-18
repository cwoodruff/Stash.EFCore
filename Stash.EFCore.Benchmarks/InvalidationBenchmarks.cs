using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Stash.EFCore.Caching;

namespace Stash.EFCore.Benchmarks;

/// <summary>
/// Benchmarks cache invalidation cost across different entry counts and table counts.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class InvalidationBenchmarks
{
    private MemoryCacheStore _store = null!;
    private IMemoryCache _memoryCache = null!;

    [GlobalSetup]
    public void Setup()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 256 * 1024 * 1024 });
        _store = new MemoryCacheStore(_memoryCache);
    }

    [GlobalCleanup]
    public void Cleanup() => _memoryCache.Dispose();

    [IterationSetup(Target = nameof(Invalidate_1Table_10Entries))]
    public void Seed_1Table_10()
    {
        ResetAndSeed("products", 10).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Invalidate 1 table, 10 entries")]
    public async Task Invalidate_1Table_10Entries()
    {
        await _store.InvalidateByTagsAsync(["products"]);
    }

    [IterationSetup(Target = nameof(Invalidate_1Table_1000Entries))]
    public void Seed_1Table_1000()
    {
        ResetAndSeed("products", 1000).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Invalidate 1 table, 1000 entries")]
    public async Task Invalidate_1Table_1000Entries()
    {
        await _store.InvalidateByTagsAsync(["products"]);
    }

    [IterationSetup(Target = nameof(Invalidate_5Tables))]
    public void Seed_5Tables()
    {
        Seed5Tables().GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Invalidate 5 tables simultaneously")]
    public async Task Invalidate_5Tables()
    {
        await _store.InvalidateByTagsAsync(["products", "categories", "orders", "customers", "suppliers"]);
    }

    private async Task ResetAndSeed(string tableName, int entryCount)
    {
        await _store.InvalidateAllAsync();

        var resultSet = CreateSmallResultSet();
        for (var i = 0; i < entryCount; i++)
        {
            await _store.SetAsync(
                $"stash:key_{tableName}_{i}",
                resultSet,
                TimeSpan.FromMinutes(30),
                tags: [tableName]);
        }
    }

    private async Task Seed5Tables()
    {
        await _store.InvalidateAllAsync();

        var resultSet = CreateSmallResultSet();
        var tables = new[] { "products", "categories", "orders", "customers", "suppliers" };
        foreach (var table in tables)
        {
            for (var i = 0; i < 100; i++)
            {
                await _store.SetAsync(
                    $"stash:key_{table}_{i}",
                    resultSet,
                    TimeSpan.FromMinutes(30),
                    tags: [table]);
            }
        }
    }

    private static CacheableResultSet CreateSmallResultSet()
    {
        return new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "INTEGER", FieldType = typeof(long) },
                new ColumnDefinition { Name = "Name", Ordinal = 1, DataTypeName = "TEXT", FieldType = typeof(string) }
            ],
            Rows = [[(object)1L, "Product-1"]],
            ApproximateSizeBytes = 128,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
