namespace Stash.EFCore.Diagnostics;

/// <summary>
/// Types of cache events raised by Stash interceptors and the invalidation API.
/// Used with <see cref="StashEvent"/> and <see cref="Configuration.StashOptions.OnStashEvent"/>.
/// </summary>
public enum StashEventType
{
    /// <summary>A query result was served from the cache.</summary>
    CacheHit,

    /// <summary>A query result was not found in the cache.</summary>
    CacheMiss,

    /// <summary>A query result was stored in the cache after a miss.</summary>
    QueryResultCached,

    /// <summary>One or more cache entries were invalidated by tag, key, or bulk clear.</summary>
    CacheInvalidated,

    /// <summary>A cache operation (read or write) threw an exception.</summary>
    CacheError,

    /// <summary>Caching was skipped because the result set exceeded <see cref="Configuration.StashOptions.MaxRowsPerQuery"/>.</summary>
    SkippedTooManyRows,

    /// <summary>Caching was skipped because the result set exceeded <see cref="Configuration.StashOptions.MaxCacheEntrySize"/>.</summary>
    SkippedTooLarge,

    /// <summary>Caching was skipped because the query touches a table in <see cref="Configuration.StashOptions.ExcludedTables"/>.</summary>
    SkippedExcludedTable,

    /// <summary>A cache error occurred but the query fell back to the database successfully.</summary>
    CacheFallbackToDb
}
