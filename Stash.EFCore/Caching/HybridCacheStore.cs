#if NET9_0_OR_GREATER
using Microsoft.Extensions.Caching.Hybrid;

namespace Stash.EFCore.Caching;

/// <summary>
/// Cache store backed by <see cref="HybridCache"/> for L1 (memory) + L2 (distributed) scenarios
/// with built-in stampede protection and tag-based invalidation.
/// Serializes <see cref="CacheableResultSet"/> to byte[] via <see cref="CacheableResultSetSerializer"/>.
/// </summary>
public class HybridCacheStore : ICacheStore
{
    private readonly HybridCache _cache;
    private volatile int _version;

    /// <summary>
    /// Options passed to GetOrCreateAsync to prevent caching the "miss" sentinel.
    /// Disables both L1 and L2 writes so the empty byte[] factory result is never stored.
    /// </summary>
    private static readonly HybridCacheEntryOptions GetOptions = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite
    };

    public HybridCacheStore(HybridCache cache)
    {
        _cache = cache;
    }

    public async Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var versionedKey = VersionedKey(key);

        var result = await _cache.GetOrCreateAsync<byte[]>(
            versionedKey,
            static _ => new ValueTask<byte[]>(Array.Empty<byte>()),
            GetOptions,
            cancellationToken: cancellationToken);

        if (result is null || result.Length == 0)
            return null;

        return CacheableResultSetSerializer.Deserialize(result);
    }

    public async Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = CacheableResultSetSerializer.Serialize(value);
        var versionedKey = VersionedKey(key);

        var options = new HybridCacheEntryOptions
        {
            Expiration = absoluteExpiration,
            LocalCacheExpiration = slidingExpiration
        };

        var tagList = tags as IReadOnlyCollection<string> ?? tags?.ToList();
        await _cache.SetAsync(versionedKey, bytes, options, tagList, cancellationToken);
    }

    public async Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        foreach (var tag in tags)
        {
            await _cache.RemoveByTagAsync(tag, cancellationToken);
        }
    }

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _version);
        return Task.CompletedTask;
    }

    private string VersionedKey(string key) => $"v{_version}:{key}";
}
#endif
