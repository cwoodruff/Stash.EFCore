using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Benchmarks.Infrastructure;
using Stash.EFCore.Caching;
using Stash.EFCore.Data;

namespace Stash.EFCore.Benchmarks;

/// <summary>
/// Benchmarks cache performance across result set sizes:
/// capture time, serialization, deserialization, and CachedDataReader replay.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class ResultSetSizesBenchmarks
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<BenchmarkDbContext> _dbOptions = null!;

    // Pre-captured result sets at each size for serialization/deserialization/replay benchmarks
    private readonly Dictionary<int, CacheableResultSet> _capturedSets = new();
    private readonly Dictionary<int, byte[]> _serializedSets = new();

    [Params(1, 10, 100, 1_000, 10_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new BenchmarkDbContext(_dbOptions);
        ctx.Database.EnsureCreated();

        // Seed enough products for the largest param
        var categories = new[] { new BenchmarkCategory { Name = "Cat1" } };
        ctx.Categories.AddRange(categories);
        ctx.SaveChanges();

        for (var i = 0; i < 10_000; i++)
        {
            ctx.Products.Add(new BenchmarkProduct
            {
                Name = $"Product-{i + 1:D5}",
                Price = 10.00m + i,
                CategoryId = 1
            });
        }
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        // Pre-capture and pre-serialize for each row count
        foreach (var size in new[] { 1, 10, 100, 1_000, 10_000 })
        {
            var rs = CaptureResultSet(size).GetAwaiter().GetResult();
            _capturedSets[size] = rs;
            _serializedSets[size] = CacheableResultSetSerializer.Serialize(rs);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark(Description = "Capture (DbDataReader → CacheableResultSet)")]
    public async Task<CacheableResultSet?> Capture()
    {
        return await CaptureResultSet(RowCount);
    }

    [Benchmark(Description = "Serialize (CacheableResultSet → byte[])")]
    public byte[] Serialize()
    {
        return CacheableResultSetSerializer.Serialize(_capturedSets[RowCount]);
    }

    [Benchmark(Description = "Deserialize (byte[] → CacheableResultSet)")]
    public CacheableResultSet? Deserialize()
    {
        return CacheableResultSetSerializer.Deserialize(_serializedSets[RowCount]);
    }

    [Benchmark(Description = "Replay (CachedDataReader iteration)")]
    public int Replay()
    {
        var reader = new CachedDataReader(_capturedSets[RowCount]);
        var count = 0;
        while (reader.Read())
        {
            // Access each column to simulate EF Core materialization
            for (var i = 0; i < reader.FieldCount; i++)
                _ = reader.GetValue(i);
            count++;
        }
        return count;
    }

    private async Task<CacheableResultSet> CaptureResultSet(int rowCount)
    {
        using var ctx = new BenchmarkDbContext(_dbOptions);
        // Use raw SQL to get exact row count without EF overhead variation
        var cmd = ctx.Database.GetDbConnection().CreateCommand();
        await ctx.Database.OpenConnectionAsync();
        cmd.CommandText = $"SELECT * FROM Products LIMIT {rowCount}";
        using var reader = await cmd.ExecuteReaderAsync();
        var result = await CacheableResultSet.CaptureAsync(reader, int.MaxValue);
        return result!;
    }
}
