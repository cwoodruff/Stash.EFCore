namespace Stash.EFCore.Caching;

/// <summary>
/// Abstraction over cache storage, supporting both in-memory and distributed caches.
/// Implementations must be thread-safe.
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Retrieves a cached result set by key, or <c>null</c> on cache miss.
    /// </summary>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The cached result set, or <c>null</c> if not found.</returns>
    Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a result set in the cache with the specified expiration and table dependency tags.
    /// </summary>
    /// <param name="key">The cache key to store under.</param>
    /// <param name="value">The result set to cache.</param>
    /// <param name="absoluteExpiration">Maximum lifetime of the cache entry.</param>
    /// <param name="slidingExpiration">Optional idle timeout that resets on each access.</param>
    /// <param name="tags">Table names this entry depends on, used for tag-based invalidation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries tagged with any of the specified table names.
    /// </summary>
    /// <param name="tags">The table name tags to invalidate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a single cache entry by its exact key.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InvalidateKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries regardless of tags.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);
}
