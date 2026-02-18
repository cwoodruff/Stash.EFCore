using Microsoft.EntityFrameworkCore;

namespace Stash.EFCore.Extensions;

/// <summary>
/// Fluent extension methods for marking EF Core queries for caching.
/// </summary>
public static class QueryableExtensions
{
    internal const string StashTag = "__stash__";

    /// <summary>
    /// Marks this query to be cached by the Stash interceptor using default expiration settings.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source)
    {
        return source.TagWith(StashTag);
    }

    /// <summary>
    /// Marks this query to be cached with a specific absolute expiration.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source, TimeSpan absoluteExpiration)
    {
        return source.TagWith($"{StashTag}:abs={absoluteExpiration.TotalSeconds}");
    }

    /// <summary>
    /// Marks this query to be cached using a named profile.
    /// </summary>
    public static IQueryable<T> Stash<T>(this IQueryable<T> source, string profileName)
    {
        return source.TagWith($"{StashTag}:profile={profileName}");
    }
}
