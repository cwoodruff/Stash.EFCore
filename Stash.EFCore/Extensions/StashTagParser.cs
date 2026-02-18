using System.Globalization;

namespace Stash.EFCore.Extensions;

/// <summary>
/// Parses Stash cache tags embedded in SQL command text by <see cref="QueryableExtensions"/>.
/// Tags are SQL comments in the form <c>-- Stash:TTL=300</c>, <c>-- Stash:TTL=300,Sliding=60</c>,
/// <c>-- Stash:Profile=hot-data</c>, or <c>-- Stash:NoCache</c>.
/// </summary>
public static class StashTagParser
{
    private const string TtlPrefix = "-- Stash:TTL=";
    private const string ProfilePrefix = "-- Stash:Profile=";
    private const string NoCacheMarker = "-- Stash:NoCache";

    /// <summary>
    /// Returns true if the command text contains a Stash caching tag
    /// (either TTL-based or profile-based).
    /// </summary>
    public static bool IsCacheable(string commandText)
    {
        return commandText.Contains(TtlPrefix, StringComparison.Ordinal) ||
               commandText.Contains(ProfilePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns true if the command text contains a Stash:NoCache tag.
    /// </summary>
    public static bool IsExplicitlyNotCached(string commandText)
    {
        return commandText.Contains(NoCacheMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses the Stash cache tag from the SQL command text.
    /// Returns null values for fields not present in the tag.
    /// </summary>
    public static (TimeSpan? AbsoluteTtl, TimeSpan? SlidingTtl, string? Profile) ParseCacheTag(string commandText)
    {
        // Check for profile-based tag first
        var profileIdx = commandText.IndexOf(ProfilePrefix, StringComparison.Ordinal);
        if (profileIdx >= 0)
        {
            var profileStart = profileIdx + ProfilePrefix.Length;
            var profileEnd = commandText.IndexOf('\n', profileStart);
            var profileName = (profileEnd < 0 ? commandText[profileStart..] : commandText[profileStart..profileEnd]).Trim();

            if (profileName.Length > 0)
                return (null, null, profileName);
        }

        // Check for TTL-based tag
        var ttlIdx = commandText.IndexOf(TtlPrefix, StringComparison.Ordinal);
        if (ttlIdx < 0)
            return (null, null, null);

        // Extract the tag line after "-- Stash:"
        var tagStart = ttlIdx + "-- Stash:".Length;
        var lineEnd = commandText.IndexOf('\n', tagStart);
        var tagLine = (lineEnd < 0 ? commandText[tagStart..] : commandText[tagStart..lineEnd]).Trim();

        TimeSpan? absoluteTtl = null;
        TimeSpan? slidingTtl = null;

        if (TryParseTagInt(tagLine, "TTL", out var ttlSeconds) && ttlSeconds > 0)
            absoluteTtl = TimeSpan.FromSeconds(ttlSeconds);

        if (TryParseTagInt(tagLine, "Sliding", out var slidingSeconds) && slidingSeconds > 0)
            slidingTtl = TimeSpan.FromSeconds(slidingSeconds);

        return (absoluteTtl, slidingTtl, null);
    }

    private static bool TryParseTagInt(string tagLine, string key, out int value)
    {
        var prefix = key + "=";
        var idx = tagLine.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            value = 0;
            return false;
        }

        var start = idx + prefix.Length;
        var end = tagLine.IndexOf(',', start);
        var str = (end < 0 ? tagLine[start..] : tagLine[start..end]).Trim();
        return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
