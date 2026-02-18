using System.Collections.Concurrent;

namespace Stash.EFCore.Diagnostics;

/// <summary>
/// Thread-safe implementation of <see cref="IStashStatistics"/> using Interlocked operations.
/// </summary>
public class StashStatistics : IStashStatistics
{
    private long _hits;
    private long _misses;
    private long _invalidations;
    private long _errors;
    private long _skips;
    private long _totalBytesCached;
    private readonly ConcurrentDictionary<string, long> _invalidationsByTable = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public long Hits => Interlocked.Read(ref _hits);

    /// <inheritdoc />
    public long Misses => Interlocked.Read(ref _misses);

    /// <inheritdoc />
    public long Invalidations => Interlocked.Read(ref _invalidations);

    /// <inheritdoc />
    public long Errors => Interlocked.Read(ref _errors);

    /// <inheritdoc />
    public long Skips => Interlocked.Read(ref _skips);

    /// <inheritdoc />
    public double HitRate
    {
        get
        {
            var total = Hits + Misses;
            return total == 0 ? 0.0 : (double)Hits / total * 100.0;
        }
    }

    /// <inheritdoc />
    public long TotalBytesCached => Interlocked.Read(ref _totalBytesCached);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, long> InvalidationsByTable =>
        _invalidationsByTable;

    /// <summary>Records a cache hit.</summary>
    public void RecordHit() => Interlocked.Increment(ref _hits);

    /// <summary>Records a cache miss.</summary>
    public void RecordMiss() => Interlocked.Increment(ref _misses);

    /// <summary>Records a cache error.</summary>
    public void RecordError() => Interlocked.Increment(ref _errors);

    /// <summary>Records a skipped cache operation (row/size limit exceeded).</summary>
    public void RecordSkip() => Interlocked.Increment(ref _skips);

    /// <summary>Records an invalidation event and updates per-table counters.</summary>
    /// <param name="tables">The table names that were invalidated.</param>
    public void RecordInvalidation(IReadOnlyList<string> tables)
    {
        Interlocked.Increment(ref _invalidations);
        foreach (var table in tables)
        {
            _invalidationsByTable.AddOrUpdate(table, 1, (_, count) => Interlocked.Increment(ref count));
        }
    }

    /// <summary>Records bytes added to the cache.</summary>
    /// <param name="bytes">The approximate byte size of the cached entry.</param>
    public void RecordCachedBytes(long bytes)
    {
        Interlocked.Add(ref _totalBytesCached, bytes);
    }

    /// <summary>Records bytes removed from the cache (eviction).</summary>
    /// <param name="bytes">The approximate byte size of the evicted entry.</param>
    public void RecordEvictedBytes(long bytes)
    {
        Interlocked.Add(ref _totalBytesCached, -bytes);
    }

    /// <inheritdoc />
    public void Reset()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _invalidations, 0);
        Interlocked.Exchange(ref _errors, 0);
        Interlocked.Exchange(ref _skips, 0);
        Interlocked.Exchange(ref _totalBytesCached, 0);
        _invalidationsByTable.Clear();
    }
}
