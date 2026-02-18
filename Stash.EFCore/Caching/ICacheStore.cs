namespace Stash.EFCore.Caching;

/// <summary>
/// Abstraction over cache storage, supporting both in-memory and distributed caches.
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Retrieves a cached result set by key, or null on cache miss.
    /// </summary>
    Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a result set in the cache with the specified expiration and table dependency tags.
    /// </summary>
    Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries tagged with any of the specified table names.
    /// </summary>
    Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a single cache entry by its exact key.
    /// </summary>
    Task InvalidateKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries regardless of tags.
    /// </summary>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);
}
