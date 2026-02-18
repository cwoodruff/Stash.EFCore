using Microsoft.Extensions.Logging;

namespace Stash.EFCore.Logging;

/// <summary>
/// Structured logging event IDs for Stash cache operations.
/// </summary>
public static class StashEventIds
{
    public static readonly EventId CacheHit = new(100, "StashCacheHit");
    public static readonly EventId CacheMiss = new(101, "StashCacheMiss");
    public static readonly EventId CacheStore = new(102, "StashCacheStore");
    public static readonly EventId CacheInvalidation = new(103, "StashCacheInvalidation");
    public static readonly EventId CacheError = new(104, "StashCacheError");
}
