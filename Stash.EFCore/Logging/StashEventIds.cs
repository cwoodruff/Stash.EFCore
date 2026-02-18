using Microsoft.Extensions.Logging;

namespace Stash.EFCore.Logging;

/// <summary>
/// Structured logging <see cref="EventId"/> constants for Stash cache operations.
/// All IDs are in the 30001-30009 range with descriptive event names.
/// </summary>
public static class StashEventIds
{
    /// <summary>A query result was served from the cache (30001).</summary>
    public static readonly EventId CacheHit = new(30001, "Stash.EFCore.CacheHit");

    /// <summary>A query result was not found in the cache (30002).</summary>
    public static readonly EventId CacheMiss = new(30002, "Stash.EFCore.CacheMiss");

    /// <summary>A query result was stored in the cache (30003).</summary>
    public static readonly EventId QueryResultCached = new(30003, "Stash.EFCore.Cached");

    /// <summary>Cache entries were invalidated (30004).</summary>
    public static readonly EventId CacheInvalidated = new(30004, "Stash.EFCore.Invalidated");

    /// <summary>A cache operation threw an exception (30005).</summary>
    public static readonly EventId CacheError = new(30005, "Stash.EFCore.Error");

    /// <summary>Caching was skipped because the result exceeded the row limit (30006).</summary>
    public static readonly EventId SkippedTooManyRows = new(30006, "Stash.EFCore.SkippedRows");

    /// <summary>Caching was skipped because the result exceeded the size limit (30007).</summary>
    public static readonly EventId SkippedTooLarge = new(30007, "Stash.EFCore.SkippedSize");

    /// <summary>Caching was skipped because the query touches an excluded table (30008).</summary>
    public static readonly EventId SkippedExcludedTable = new(30008, "Stash.EFCore.Excluded");

    /// <summary>The query fell back to the database after a cache error (30009).</summary>
    public static readonly EventId CacheFallbackToDb = new(30009, "Stash.EFCore.Fallback");
}
