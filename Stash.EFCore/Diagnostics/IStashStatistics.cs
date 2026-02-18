namespace Stash.EFCore.Diagnostics;

/// <summary>
/// Provides thread-safe, real-time cache performance counters.
/// </summary>
public interface IStashStatistics
{
    /// <summary>Total number of cache hits.</summary>
    long Hits { get; }

    /// <summary>Total number of cache misses.</summary>
    long Misses { get; }

    /// <summary>Total number of cache invalidations (by tag, key, or all).</summary>
    long Invalidations { get; }

    /// <summary>Total number of cache errors.</summary>
    long Errors { get; }

    /// <summary>Total number of queries skipped due to row count or size limits.</summary>
    long Skips { get; }

    /// <summary>
    /// Hit rate as a percentage (0-100). Returns 0 if no requests have been made.
    /// </summary>
    double HitRate { get; }

    /// <summary>
    /// Approximate total bytes currently stored in cache.
    /// </summary>
    long TotalBytesCached { get; }

    /// <summary>
    /// Returns per-table invalidation counts.
    /// </summary>
    IReadOnlyDictionary<string, long> InvalidationsByTable { get; }

    /// <summary>
    /// Resets all counters to zero.
    /// </summary>
    void Reset();
}
