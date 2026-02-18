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
    /// Marks this query for caching with default expiration settings.
    /// </summary>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query)
    {
        return query.TagWith("Stash:TTL=0");
    }

    /// <summary>
    /// Marks this query for caching with the specified absolute TTL.
    /// </summary>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query, TimeSpan absoluteExpiration)
    {
        var seconds = (int)absoluteExpiration.TotalSeconds;
        return query.TagWith($"Stash:TTL={seconds}");
    }

    /// <summary>
    /// Marks this query for caching with absolute and sliding expiration.
    /// </summary>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query,
        TimeSpan absoluteExpiration, TimeSpan slidingExpiration)
    {
        var ttl = (int)absoluteExpiration.TotalSeconds;
        var sliding = (int)slidingExpiration.TotalSeconds;
        return query.TagWith($"Stash:TTL={ttl},Sliding={sliding}");
    }

    /// <summary>
    /// Marks this query for caching with a named cache profile.
    /// </summary>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query, string profileName)
    {
        return query.TagWith($"Stash:Profile={profileName}");
    }

    /// <summary>
    /// Marks this query to NOT be cached (useful when CacheAllQueries is true).
    /// </summary>
    public static IQueryable<T> NoStash<T>(this IQueryable<T> query)
    {
        return query.TagWith(NoCacheTag);
    }
}
