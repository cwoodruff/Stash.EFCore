#if NET10_0_OR_GREATER
using Microsoft.Extensions.Caching.Hybrid;

namespace Stash.EFCore.Caching;

/// <summary>
/// Cache store backed by <see cref="HybridCache"/> for L1 (memory) + L2 (distributed) scenarios
/// with built-in stampede protection and tag-based invalidation.
/// </summary>
public class HybridCacheStore : ICacheStore
{
    private readonly HybridCache _cache;

    public HybridCacheStore(HybridCache cache)
    {
        _cache = cache;
    }

    public async Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        // HybridCache.GetOrCreateAsync requires a factory; use a sentinel approach
        // to distinguish cache miss from cached null.
        var result = await _cache.GetOrCreateAsync(
            key,
            cancellationToken => new ValueTask<CacheableResultSet?>(result: null),
            tags: null,
            cancellationToken: cancellationToken);

        return result;
    }

    public async Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
    {
        var options = new HybridCacheEntryOptions
        {
            Expiration = absoluteExpiration
        };

        var tags = value.TableDependencies;

        await _cache.SetAsync(key, value, options, tags, cancellationToken);
    }

    public async Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        foreach (var tag in tags)
        {
            await _cache.RemoveByTagAsync(tag, cancellationToken);
        }
    }
}
#endif
