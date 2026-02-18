using Stash.EFCore.Caching;

namespace Stash.EFCore.Tests.Fakes;

/// <summary>
/// A cache store that always throws, for testing FallbackToDatabase behavior.
/// </summary>
internal sealed class FailingCacheStore : ICacheStore
{
    public Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Cache unavailable");

    public Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Cache unavailable");

    public Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Cache unavailable");

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Cache unavailable");
}
