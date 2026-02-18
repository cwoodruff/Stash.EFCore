using Microsoft.EntityFrameworkCore;

namespace Stash.EFCore.Extensions;

/// <summary>
/// Fluent extension methods for marking EF Core queries for caching.
/// Tags are embedded into the SQL command text via <see cref="EntityFrameworkQueryableExtensions.TagWith{T}"/>
/// and parsed by the <see cref="Interceptors.StashCommandInterceptor"/>.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Prefix used by EF Core when emitting TagWith comments.
    /// The interceptor searches for this in the CommandText.
    /// </summary>
    internal const string StashTagPrefix = "-- Stash:";

    /// <summary>
    /// Tag that disables caching for a specific query, even when CacheAllQueries is enabled.
    /// </summary>
    internal const string NoCacheTag = "Stash:NoCache";

    /// <summary>
    /// Marks this query to be cached using default expiration settings.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source)
    {
        return source.TagWith(FormatTag(0, 0, null));
    }

    /// <summary>
    /// Marks this query to be cached with a specific absolute expiration.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source, TimeSpan absoluteExpiration)
    {
        return source.TagWith(FormatTag((int)absoluteExpiration.TotalSeconds, 0, null));
    }

    /// <summary>
    /// Marks this query to be cached with specific absolute and sliding expirations.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source,
        TimeSpan absoluteExpiration, TimeSpan slidingExpiration)
    {
        return source.TagWith(FormatTag(
            (int)absoluteExpiration.TotalSeconds,
            (int)slidingExpiration.TotalSeconds,
            null));
    }

    /// <summary>
    /// Marks this query to be cached using a named profile.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source, string profileName)
    {
        return source.TagWith(FormatTag(0, 0, profileName));
    }

    /// <summary>
    /// Marks this query to NOT be cached, even when <c>CacheAllQueries</c> is enabled.
    /// </summary>
    public static IQueryable<T> StashNoCache<T>(this IQueryable<T> source)
    {
        return source.TagWith(NoCacheTag);
    }

    private static string FormatTag(int ttlSeconds, int slidingSeconds, string? profileName)
    {
        return $"Stash:TTL={ttlSeconds},Sliding={slidingSeconds},Profile={profileName}";
    }
}
