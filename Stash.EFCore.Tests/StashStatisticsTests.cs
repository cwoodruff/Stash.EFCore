using FluentAssertions;
using Stash.EFCore.Diagnostics;
using Xunit;

namespace Stash.EFCore.Tests;

public class StashStatisticsTests
{
    private readonly StashStatistics _stats = new();

    [Fact]
    public void InitialState_AllCountersAreZero()
    {
        _stats.Hits.Should().Be(0);
        _stats.Misses.Should().Be(0);
        _stats.Invalidations.Should().Be(0);
        _stats.Errors.Should().Be(0);
        _stats.Skips.Should().Be(0);
        _stats.TotalBytesCached.Should().Be(0);
        _stats.HitRate.Should().Be(0.0);
        _stats.InvalidationsByTable.Should().BeEmpty();
    }

    [Fact]
    public void RecordHit_IncrementsHitCounter()
    {
        _stats.RecordHit();
        _stats.RecordHit();
        _stats.RecordHit();

        _stats.Hits.Should().Be(3);
    }

    [Fact]
    public void RecordMiss_IncrementsMissCounter()
    {
        _stats.RecordMiss();
        _stats.RecordMiss();

        _stats.Misses.Should().Be(2);
    }

    [Fact]
    public void RecordError_IncrementsErrorCounter()
    {
        _stats.RecordError();

        _stats.Errors.Should().Be(1);
    }

    [Fact]
    public void RecordSkip_IncrementsSkipCounter()
    {
        _stats.RecordSkip();
        _stats.RecordSkip();

        _stats.Skips.Should().Be(2);
    }

    [Fact]
    public void HitRate_CalculatesCorrectPercentage()
    {
        _stats.RecordHit();
        _stats.RecordHit();
        _stats.RecordHit();
        _stats.RecordMiss();

        _stats.HitRate.Should().Be(75.0);
    }

    [Fact]
    public void HitRate_NoRequests_ReturnsZero()
    {
        _stats.HitRate.Should().Be(0.0);
    }

    [Fact]
    public void HitRate_AllHits_Returns100()
    {
        _stats.RecordHit();
        _stats.RecordHit();

        _stats.HitRate.Should().Be(100.0);
    }

    [Fact]
    public void HitRate_AllMisses_ReturnsZero()
    {
        _stats.RecordMiss();
        _stats.RecordMiss();

        _stats.HitRate.Should().Be(0.0);
    }

    [Fact]
    public void RecordCachedBytes_TracksTotalBytes()
    {
        _stats.RecordCachedBytes(1024);
        _stats.RecordCachedBytes(2048);

        _stats.TotalBytesCached.Should().Be(3072);
    }

    [Fact]
    public void RecordEvictedBytes_DecreasesTotalBytes()
    {
        _stats.RecordCachedBytes(5000);
        _stats.RecordEvictedBytes(2000);

        _stats.TotalBytesCached.Should().Be(3000);
    }

    [Fact]
    public void RecordInvalidation_IncrementsCounterAndTracksTables()
    {
        _stats.RecordInvalidation(["products", "categories"]);
        _stats.RecordInvalidation(["products"]);

        _stats.Invalidations.Should().Be(2);
        _stats.InvalidationsByTable.Should().ContainKey("products");
        _stats.InvalidationsByTable["products"].Should().Be(2);
        _stats.InvalidationsByTable["categories"].Should().Be(1);
    }

    [Fact]
    public void RecordInvalidation_EmptyTables_OnlyIncrementsCounter()
    {
        _stats.RecordInvalidation([]);

        _stats.Invalidations.Should().Be(1);
        _stats.InvalidationsByTable.Should().BeEmpty();
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        _stats.RecordHit();
        _stats.RecordMiss();
        _stats.RecordError();
        _stats.RecordSkip();
        _stats.RecordCachedBytes(1024);
        _stats.RecordInvalidation(["products"]);

        _stats.Reset();

        _stats.Hits.Should().Be(0);
        _stats.Misses.Should().Be(0);
        _stats.Errors.Should().Be(0);
        _stats.Skips.Should().Be(0);
        _stats.Invalidations.Should().Be(0);
        _stats.TotalBytesCached.Should().Be(0);
        _stats.InvalidationsByTable.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        const int iterations = 10_000;
        var tasks = new List<Task>();

        for (var i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < iterations; j++)
                {
                    _stats.RecordHit();
                    _stats.RecordMiss();
                    _stats.RecordError();
                    _stats.RecordSkip();
                    _stats.RecordCachedBytes(10);
                    _stats.RecordInvalidation(["table1"]);
                }
            }));
        }

        await Task.WhenAll(tasks);

        _stats.Hits.Should().Be(4 * iterations);
        _stats.Misses.Should().Be(4 * iterations);
        _stats.Errors.Should().Be(4 * iterations);
        _stats.Skips.Should().Be(4 * iterations);
        _stats.TotalBytesCached.Should().Be(4 * iterations * 10);
        _stats.Invalidations.Should().Be(4 * iterations);
    }

    [Fact]
    public void InvalidationsByTable_CaseInsensitive()
    {
        _stats.RecordInvalidation(["Products"]);
        _stats.RecordInvalidation(["products"]);
        _stats.RecordInvalidation(["PRODUCTS"]);

        _stats.InvalidationsByTable.Should().ContainKey("Products");
        // ConcurrentDictionary with OrdinalIgnoreCase treats all as the same key
        _stats.InvalidationsByTable["products"].Should().Be(3);
    }
}
