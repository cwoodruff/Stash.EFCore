using System.Data;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;

namespace Stash.EFCore.Benchmarks;

/// <summary>
/// Benchmarks cache key generation performance across query complexity levels.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class KeyGenerationBenchmarks
{
    private DefaultCacheKeyGenerator _keyGen = null!;
    private DbCommand _simpleCommand = null!;
    private DbCommand _fiveParamCommand = null!;
    private DbCommand _twentyParamCommand = null!;
    private DbCommand _longSqlCommand = null!;

    [GlobalSetup]
    public void Setup()
    {
        _keyGen = new DefaultCacheKeyGenerator(new StashOptions());

        // Simple query — no parameters
        _simpleCommand = CreateCommand(
            "SELECT * FROM Products WHERE IsActive = 1");

        // 5 parameters
        _fiveParamCommand = CreateCommand(
            "SELECT * FROM Products WHERE CategoryId = @p0 AND Price >= @p1 AND Price <= @p2 AND IsActive = @p3 AND Name LIKE @p4",
            ("@p0", DbType.Int32, 1),
            ("@p1", DbType.Decimal, 10.00m),
            ("@p2", DbType.Decimal, 100.00m),
            ("@p3", DbType.Boolean, true),
            ("@p4", DbType.String, "%Widget%"));

        // 20 parameters
        var longSql = "SELECT * FROM Products WHERE Id IN (" +
            string.Join(", ", Enumerable.Range(0, 20).Select(i => $"@p{i}")) + ")";
        var twentyParams = Enumerable.Range(0, 20)
            .Select(i => ($"@p{i}", DbType.Int32, (object)(i + 1)))
            .ToArray();
        _twentyParamCommand = CreateCommand(longSql, twentyParams);

        // Very long SQL (5000+ chars)
        var veryLongSql = "SELECT " +
            string.Join(", ", Enumerable.Range(0, 100).Select(i => $"Column{i:D3}")) +
            " FROM Products p " +
            string.Join(" ", Enumerable.Range(0, 50).Select(i =>
                $"LEFT JOIN Table{i:D3} t{i} ON p.Id = t{i}.ProductId")) +
            " WHERE " +
            string.Join(" AND ", Enumerable.Range(0, 50).Select(i =>
                $"t{i}.Value{i} IS NOT NULL"));
        _longSqlCommand = CreateCommand(veryLongSql);
    }

    [Benchmark(Baseline = true, Description = "Simple (no params)")]
    public string SimpleQuery() => _keyGen.GenerateKey(_simpleCommand);

    [Benchmark(Description = "5 parameters")]
    public string FiveParams() => _keyGen.GenerateKey(_fiveParamCommand);

    [Benchmark(Description = "20 parameters")]
    public string TwentyParams() => _keyGen.GenerateKey(_twentyParamCommand);

    [Benchmark(Description = "Long SQL (5000+ chars)")]
    public string LongSql() => _keyGen.GenerateKey(_longSqlCommand);

    private static DbCommand CreateCommand(string sql, params (string name, DbType type, object value)[] parameters)
    {
        var cmd = new SqliteCommand { CommandText = sql };
        foreach (var (name, type, value) in parameters)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.DbType = type;
            param.Value = value;
            cmd.Parameters.Add(param);
        }
        return cmd;
    }
}
