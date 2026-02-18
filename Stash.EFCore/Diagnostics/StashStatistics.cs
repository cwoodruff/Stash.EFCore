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

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Invalidations => Interlocked.Read(ref _invalidations);
    public long Errors => Interlocked.Read(ref _errors);
    public long Skips => Interlocked.Read(ref _skips);

    public double HitRate
    {
        get
        {
            var total = Hits + Misses;
            return total == 0 ? 0.0 : (double)Hits / total * 100.0;
        }
    }

    public long TotalBytesCached => Interlocked.Read(ref _totalBytesCached);

    public IReadOnlyDictionary<string, long> InvalidationsByTable =>
        _invalidationsByTable;

    public void RecordHit() => Interlocked.Increment(ref _hits);

    public void RecordMiss() => Interlocked.Increment(ref _misses);

    public void RecordError() => Interlocked.Increment(ref _errors);

    public void RecordSkip() => Interlocked.Increment(ref _skips);

    public void RecordInvalidation(IReadOnlyList<string> tables)
    {
        Interlocked.Increment(ref _invalidations);
        foreach (var table in tables)
        {
            _invalidationsByTable.AddOrUpdate(table, 1, (_, count) => Interlocked.Increment(ref count));
        }
    }

    public void RecordCachedBytes(long bytes)
    {
        Interlocked.Add(ref _totalBytesCached, bytes);
    }

    public void RecordEvictedBytes(long bytes)
    {
        Interlocked.Add(ref _totalBytesCached, -bytes);
    }

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
