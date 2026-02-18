namespace Stash.EFCore.Diagnostics;

/// <summary>
/// Represents a cache event raised by Stash, used with the <see cref="Configuration.StashOptions.OnStashEvent"/> callback.
/// </summary>
public sealed record StashEvent
{
    /// <summary>
    /// The type of cache event.
    /// </summary>
    public required StashEventType EventType { get; init; }

    /// <summary>
    /// The cache key associated with this event, if applicable.
    /// </summary>
    public string? CacheKey { get; init; }

    /// <summary>
    /// The table names associated with this event (e.g., tables invalidated or depended upon).
    /// </summary>
    public IReadOnlyCollection<string>? Tables { get; init; }

    /// <summary>
    /// Number of rows in the cached result set, if applicable.
    /// </summary>
    public int? RowCount { get; init; }

    /// <summary>
    /// Approximate size in bytes of the cached entry, if applicable.
    /// </summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// The TTL applied to the cached entry, if applicable.
    /// </summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>
    /// Duration of the cache operation (e.g., time to read from or write to cache).
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// The exception, if this event represents an error.
    /// </summary>
    public Exception? Exception { get; init; }
}
