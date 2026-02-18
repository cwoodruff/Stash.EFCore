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
    /// <param name="tableNames">The table names whose cached queries should be invalidated.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <example>
    /// <code>
    /// await invalidator.InvalidateTablesAsync(["products", "categories"]);
    /// </code>
    /// </example>
    Task InvalidateTablesAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries for the tables mapped to the specified entity types
    /// within the given <typeparamref name="TContext"/>.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to resolve table names from.</typeparam>
    /// <param name="context">The DbContext instance for model metadata lookup.</param>
    /// <param name="entityTypes">The CLR entity types whose table caches should be invalidated.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <example>
    /// <code>
    /// await invalidator.InvalidateEntitiesAsync(db, [typeof(Product), typeof(Category)]);
    /// </code>
    /// </example>
    Task InvalidateEntitiesAsync<TContext>(TContext context, IEnumerable<Type> entityTypes, CancellationToken cancellationToken = default)
        where TContext : DbContext;

    /// <summary>
    /// Invalidates a single cache entry by its exact cache key.
    /// </summary>
    /// <param name="cacheKey">The full cache key to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries regardless of tags or keys.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);
}
