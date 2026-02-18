using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Logging;

namespace Stash.EFCore.Caching;

/// <summary>
/// Default implementation of <see cref="IStashInvalidator"/> that delegates
/// to the configured <see cref="ICacheStore"/>.
/// </summary>
public class StashInvalidator : IStashInvalidator
{
    private readonly ICacheStore _cacheStore;
    private readonly ILogger<StashInvalidator> _logger;

    public StashInvalidator(ICacheStore cacheStore, ILogger<StashInvalidator> logger)
    {
        _cacheStore = cacheStore;
        _logger = logger;
    }

    public async Task InvalidateTablesAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        var tables = tableNames as IReadOnlyList<string> ?? tableNames.ToList();

        _logger.LogDebug(StashEventIds.CacheInvalidation,
            "Manual invalidation for tables: [{Tables}]",
            string.Join(", ", tables));

        await _cacheStore.InvalidateByTagsAsync(tables, cancellationToken);
    }

    public async Task InvalidateEntitiesAsync<TContext>(TContext context, IEnumerable<Type> entityTypes, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var clrType in entityTypes)
        {
            var entityType = context.Model.FindEntityType(clrType);
            var tableName = entityType?.GetTableName();
            if (tableName is not null)
                tableNames.Add(tableName.ToLowerInvariant());
        }

        if (tableNames.Count > 0)
        {
            _logger.LogDebug(StashEventIds.CacheInvalidation,
                "Manual entity invalidation for tables: [{Tables}]",
                string.Join(", ", tableNames));

            await _cacheStore.InvalidateByTagsAsync(tableNames, cancellationToken);
        }
    }

    public async Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(StashEventIds.CacheInvalidation,
            "Manual invalidation for key: {Key}", cacheKey);

        await _cacheStore.InvalidateKeyAsync(cacheKey, cancellationToken);
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(StashEventIds.CacheInvalidation, "Manual invalidation of all cache entries");

        await _cacheStore.InvalidateAllAsync(cancellationToken);
    }
}
