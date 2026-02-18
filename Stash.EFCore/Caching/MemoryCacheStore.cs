using Microsoft.Extensions.Caching.Memory;

namespace Stash.EFCore.Caching;

/// <summary>
/// Cache store backed by <see cref="IMemoryCache"/> for single-server scenarios.
/// </summary>
public class MemoryCacheStore : ICacheStore
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentTagIndex _tagIndex = new();

    public MemoryCacheStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<CacheableResultSet?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(key, out CacheableResultSet? result);
        return Task.FromResult(result);
    }

    public Task SetAsync(string key, CacheableResultSet value, TimeSpan absoluteExpiration, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpiration
        };

        if (slidingExpiration.HasValue)
            options.SlidingExpiration = slidingExpiration.Value;

        options.RegisterPostEvictionCallback((k, _, _, _) =>
        {
            if (k is string keyStr)
                _tagIndex.RemoveKey(keyStr);
        });

        _cache.Set(key, value, options);
        _tagIndex.Add(key, value.TableDependencies);

        return Task.CompletedTask;
    }

    public Task InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        foreach (var key in _tagIndex.GetKeysByTags(tags))
        {
            _cache.Remove(key);
            _tagIndex.RemoveKey(key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Thread-safe index mapping cache keys to table dependency tags and vice versa.
    /// </summary>
    private sealed class ConcurrentTagIndex
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _keyToTags = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _tagToKeys = new();
        private readonly object _lock = new();

        public void Add(string key, IEnumerable<string> tags)
        {
            lock (_lock)
            {
                var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
                _keyToTags[key] = tagSet;

                foreach (var tag in tagSet)
                {
                    var keys = _tagToKeys.GetOrAdd(tag, _ => new HashSet<string>());
                    keys.Add(key);
                }
            }
        }

        public IEnumerable<string> GetKeysByTags(IEnumerable<string> tags)
        {
            var result = new HashSet<string>();
            lock (_lock)
            {
                foreach (var tag in tags)
                {
                    if (_tagToKeys.TryGetValue(tag, out var keys))
                        result.UnionWith(keys);
                }
            }
            return result;
        }

        public void RemoveKey(string key)
        {
            lock (_lock)
            {
                if (_keyToTags.TryRemove(key, out var tags))
                {
                    foreach (var tag in tags)
                    {
                        if (_tagToKeys.TryGetValue(tag, out var keys))
                        {
                            keys.Remove(key);
                            if (keys.Count == 0)
                                _tagToKeys.TryRemove(tag, out _);
                        }
                    }
                }
            }
        }
    }
}
