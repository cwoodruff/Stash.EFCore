namespace Stash.EFCore.Configuration;

/// <summary>
/// Global configuration options for Stash query caching.
/// </summary>
public class StashOptions
{
    /// <summary>
    /// Default absolute expiration for cached queries.
    /// </summary>
    public TimeSpan DefaultAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Default sliding expiration for cached queries. Null disables sliding expiration.
    /// </summary>
    public TimeSpan? DefaultSlidingExpiration { get; set; }

    /// <summary>
    /// Prefix for all cache keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "stash:";

    /// <summary>
    /// When true, all queries are cached automatically without requiring .Stash().
    /// </summary>
    public bool EnableGlobalCaching { get; set; }

    /// <summary>
    /// When true, cache hit/miss events are logged via ILogger.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Named caching profiles for per-query configuration.
    /// </summary>
    public Dictionary<string, StashProfile> Profiles { get; set; } = new();
}
