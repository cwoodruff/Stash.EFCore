using Microsoft.Extensions.Logging;

namespace Stash.EFCore.Logging;

/// <summary>
/// Structured logging event IDs for Stash cache operations.
/// </summary>
public static class StashEventIds
{
    public static readonly EventId CacheHit = new(30001, "Stash.EFCore.CacheHit");
    public static readonly EventId CacheMiss = new(30002, "Stash.EFCore.CacheMiss");
    public static readonly EventId QueryResultCached = new(30003, "Stash.EFCore.Cached");
    public static readonly EventId CacheInvalidated = new(30004, "Stash.EFCore.Invalidated");
    public static readonly EventId CacheError = new(30005, "Stash.EFCore.Error");
    public static readonly EventId SkippedTooManyRows = new(30006, "Stash.EFCore.SkippedRows");
    public static readonly EventId SkippedTooLarge = new(30007, "Stash.EFCore.SkippedSize");
    public static readonly EventId SkippedExcludedTable = new(30008, "Stash.EFCore.Excluded");
    public static readonly EventId CacheFallbackToDb = new(30009, "Stash.EFCore.Fallback");
}
