namespace Stash.EFCore.Configuration;

/// <summary>
/// A named caching profile that can be referenced by queries for per-query cache settings.
/// </summary>
public class StashProfile
{
    /// <summary>
    /// The unique name of this profile, used with .Cached("profileName").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Absolute expiration for queries using this profile. Overrides <see cref="StashOptions.DefaultAbsoluteExpiration"/>.
    /// </summary>
    public TimeSpan? AbsoluteExpiration { get; set; }

    /// <summary>
    /// Sliding expiration for queries using this profile. Overrides <see cref="StashOptions.DefaultSlidingExpiration"/>.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }
}
