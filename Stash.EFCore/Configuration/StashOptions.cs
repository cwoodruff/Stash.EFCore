using Stash.EFCore.Diagnostics;

namespace Stash.EFCore.Configuration;

/// <summary>
/// Global configuration options for Stash query caching.
/// </summary>
public class StashOptions
{
    /// <summary>
    /// Default absolute expiration for cached queries.
    /// </summary>
    public TimeSpan DefaultAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Default sliding expiration for cached queries. Null disables sliding expiration.
    /// </summary>
    public TimeSpan? DefaultSlidingExpiration { get; set; }

    /// <summary>
    /// Prefix for all cache keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "stash:";

    /// <summary>
    /// When true, all SELECT queries are cached automatically without requiring <c>.Cached()</c>.
    /// Queries targeting tables in <see cref="ExcludedTables"/> are still excluded.
    /// </summary>
    public bool CacheAllQueries { get; set; }

    /// <summary>
    /// Table names that should never be cached when <see cref="CacheAllQueries"/> is true.
    /// Has no effect on queries explicitly tagged with <c>.Cached()</c>.
    /// </summary>
    public HashSet<string> ExcludedTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When true, cache hit/miss events are logged via ILogger.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Maximum number of rows to cache per query. Queries returning more rows are not cached
    /// but their results are still returned to EF Core.
    /// </summary>
    public int MaxRowsPerQuery { get; set; } = 10_000;

    /// <summary>
    /// Maximum size in bytes for a single cache entry. Entries larger than this are not cached.
    /// Set to 0 (default) to disable the size limit.
    /// </summary>
    public long MaxCacheEntrySize { get; set; }

    /// <summary>
    /// When true (default), cache store exceptions are caught and the query falls back to the database.
    /// When false, cache store exceptions propagate to the caller.
    /// </summary>
    public bool FallbackToDatabase { get; set; } = true;

    /// <summary>
    /// Named caching profiles for per-query configuration.
    /// </summary>
    public Dictionary<string, StashProfile> Profiles { get; set; } = new();

    /// <summary>
    /// Optional callback invoked on every cache event (hit, miss, invalidation, error, etc.).
    /// Use for custom telemetry, metrics, or alerting.
    /// </summary>
    public Action<StashEvent>? OnStashEvent { get; set; }

    /// <summary>
    /// Minimum cache hit rate (0-100) for the health check to report Healthy.
    /// Below this threshold, the health check reports Degraded.
    /// Default is 10%.
    /// </summary>
    public double MinimumHitRatePercent { get; set; } = 10.0;
}
