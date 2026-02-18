using Microsoft.EntityFrameworkCore;

namespace Stash.EFCore.Caching;

/// <summary>
/// Manual cache invalidation API for scenarios not covered by automatic
/// SaveChanges interception, such as ExecuteUpdate/ExecuteDelete or
/// direct SQL changes.
/// </summary>
public interface IStashInvalidator
{
    /// <summary>
    /// Invalidates all cache entries tagged with the specified table names.
    /// Table names should be lowercased for consistent matching.
    /// </summary>
    Task InvalidateTablesAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries for the tables mapped to the specified entity types
    /// within the given <typeparamref name="TContext"/>.
    /// </summary>
    Task InvalidateEntitiesAsync<TContext>(TContext context, IEnumerable<Type> entityTypes, CancellationToken cancellationToken = default)
        where TContext : DbContext;

    /// <summary>
    /// Invalidates a single cache entry by its exact cache key.
    /// </summary>
    Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries regardless of tags or keys.
    /// </summary>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);
}
