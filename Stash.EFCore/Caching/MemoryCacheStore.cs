using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Stash.EFCore.Caching;

/// <summary>
/// Cache store backed by <see cref="IMemoryCache"/> for single-server scenarios.
/// Uses a version counter for <see cref="InvalidateAllAsync"/> and a
/// <see cref="SemaphoreSlim"/>-protected tag index for table-based invalidation.
/// </summary>
public class MemoryCacheStore : ICacheStore
{
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _tagLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagToKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _keyToTags = new();
    private long _version;

    /// <summary>
    /// Wrapper stored in IMemoryCache so GetAsync can check the version counter.
    /// </summary>
    private sealed record CacheEntry(CacheableResultSet Value, long Version);

    public MemoryCacheStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out CacheEntry? entry) && entry is not null)
        {
            if (entry.Version == Interlocked.Read(ref _version))
                return Task.FromResult<CacheableResultSet?>(entry.Value);

            // Version mismatch — logically expired by InvalidateAllAsync
            _cache.Remove(key);
        }

        return Task.FromResult<CacheableResultSet?>(null);
    }

    public async Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration,
        TimeSpan? slidingExpiration = null, IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = Interlocked.Read(ref _version);
        var entry = new CacheEntry(value, currentVersion);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpiration,
            Size = value.ApproximateSizeBytes
        };

        if (slidingExpiration.HasValue)
            options.SlidingExpiration = slidingExpiration.Value;

        options.RegisterPostEvictionCallback((k, _, _, _) =>
        {
            if (k is string keyStr)
                TryRemoveKeyFromTagIndex(keyStr);
        });

        if (tags is { Count: > 0 })
        {
            await _tagLock.WaitAsync(cancellationToken);
            try
            {
                TryRemoveKeyFromTagIndex(key);

                var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
                _keyToTags[key] = tagSet;

                foreach (var tag in tagSet)
                {
                    var keys = _tagToKeys.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>());
                    keys[key] = 0;
                }
            }
            finally
            {
                _tagLock.Release();
            }
        }

        _cache.Set(key, entry, options);
    }

    public async Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        await _tagLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var tag in tags)
            {
                if (_tagToKeys.TryRemove(tag, out var keys))
                {
                    foreach (var key in keys.Keys)
                    {
                        _cache.Remove(key);

                        if (_keyToTags.TryRemove(key, out var keyTags))
                        {
                            foreach (var otherTag in keyTags)
                            {
                                if (!string.Equals(otherTag, tag, StringComparison.OrdinalIgnoreCase) &&
                                    _tagToKeys.TryGetValue(otherTag, out var otherKeys))
                                {
                                    otherKeys.TryRemove(key, out _);
                                    if (otherKeys.IsEmpty)
                                        _tagToKeys.TryRemove(otherTag, out _);
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            _tagLock.Release();
        }
    }

    public Task InvalidateKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        TryRemoveKeyFromTagIndex(key);
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _version);
        _tagToKeys.Clear();
        _keyToTags.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Best-effort cleanup of tag index entries for a key. Called from PostEvictionCallback
    /// (without holding the semaphore) and from SetAsync (while holding it).
    /// Uses only lock-free ConcurrentDictionary operations.
    /// </summary>
    private void TryRemoveKeyFromTagIndex(string key)
    {
        if (_keyToTags.TryRemove(key, out var tags))
        {
            foreach (var tag in tags)
            {
                if (_tagToKeys.TryGetValue(tag, out var keys))
                {
                    keys.TryRemove(key, out _);
                    if (keys.IsEmpty)
                        _tagToKeys.TryRemove(tag, out _);
                }
            }
        }
    }
}
