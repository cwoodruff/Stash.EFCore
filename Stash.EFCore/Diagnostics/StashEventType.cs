namespace Stash.EFCore.Diagnostics;

/// <summary>
/// Types of cache events raised by Stash.
/// </summary>
public enum StashEventType
{
    CacheHit,
    CacheMiss,
    QueryResultCached,
    CacheInvalidated,
    CacheError,
    SkippedTooManyRows,
    SkippedTooLarge,
    SkippedExcludedTable,
    CacheFallbackToDb
}
