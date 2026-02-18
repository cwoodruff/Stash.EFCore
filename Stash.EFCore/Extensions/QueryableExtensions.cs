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
    /// Marks this query for caching with the default expiration from <see cref="Configuration.StashOptions"/>.
    /// </summary>
    /// <typeparam name="T">The entity type of the query.</typeparam>
    /// <param name="query">The queryable to cache.</param>
    /// <returns>The queryable with a Stash cache tag appended.</returns>
    /// <example>
    /// <code>
    /// var products = await db.Products
    ///     .Where(p => p.IsActive)
    ///     .Cached()
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query)
    {
        return query.TagWith("Stash:TTL=0");
    }

    /// <summary>
    /// Marks this query for caching with the specified absolute TTL.
    /// </summary>
    /// <typeparam name="T">The entity type of the query.</typeparam>
    /// <param name="query">The queryable to cache.</param>
    /// <param name="absoluteExpiration">How long the cached result remains valid.</param>
    /// <returns>The queryable with a Stash cache tag appended.</returns>
    /// <example>
    /// <code>
    /// var products = await db.Products
    ///     .Cached(TimeSpan.FromMinutes(5))
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query, TimeSpan absoluteExpiration)
    {
        var seconds = (int)absoluteExpiration.TotalSeconds;
        return query.TagWith($"Stash:TTL={seconds}");
    }

    /// <summary>
    /// Marks this query for caching with absolute and sliding expiration.
    /// </summary>
    /// <typeparam name="T">The entity type of the query.</typeparam>
    /// <param name="query">The queryable to cache.</param>
    /// <param name="absoluteExpiration">Maximum lifetime of the cached result.</param>
    /// <param name="slidingExpiration">Idle timeout; resets on each cache hit.</param>
    /// <returns>The queryable with a Stash cache tag appended.</returns>
    /// <example>
    /// <code>
    /// var products = await db.Products
    ///     .Cached(TimeSpan.FromHours(1), TimeSpan.FromMinutes(10))
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query,
        TimeSpan absoluteExpiration, TimeSpan slidingExpiration)
    {
        var ttl = (int)absoluteExpiration.TotalSeconds;
        var sliding = (int)slidingExpiration.TotalSeconds;
        return query.TagWith($"Stash:TTL={ttl},Sliding={sliding}");
    }

    /// <summary>
    /// Marks this query for caching with a named cache profile defined in <see cref="Configuration.StashOptions.Profiles"/>.
    /// </summary>
    /// <typeparam name="T">The entity type of the query.</typeparam>
    /// <param name="query">The queryable to cache.</param>
    /// <param name="profileName">The name of a registered <see cref="Configuration.StashProfile"/>.</param>
    /// <returns>The queryable with a Stash cache tag appended.</returns>
    /// <example>
    /// <code>
    /// var products = await db.Products
    ///     .Cached("hot-data")
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> Cached<T>(this IQueryable<T> query, string profileName)
    {
        return query.TagWith($"Stash:Profile={profileName}");
    }

    /// <summary>
    /// Marks this query to NOT be cached, even when <see cref="Configuration.StashOptions.CacheAllQueries"/> is enabled.
    /// </summary>
    /// <typeparam name="T">The entity type of the query.</typeparam>
    /// <param name="query">The queryable to exclude from caching.</param>
    /// <returns>The queryable with a NoCache tag appended.</returns>
    /// <example>
    /// <code>
    /// var auditLogs = await db.AuditLogs
    ///     .NoStash()
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> NoStash<T>(this IQueryable<T> query)
    {
        return query.TagWith(NoCacheTag);
    }
}
