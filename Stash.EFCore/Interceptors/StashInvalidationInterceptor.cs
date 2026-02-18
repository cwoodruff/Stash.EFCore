using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Caching;
using Stash.EFCore.Logging;

namespace Stash.EFCore.Interceptors;

/// <summary>
/// Intercepts SaveChanges to automatically invalidate cached queries
/// whose table dependencies overlap with the modified entity types.
/// </summary>
public class StashInvalidationInterceptor : SaveChangesInterceptor
{
    private readonly ICacheStore _cacheStore;
    private readonly ILogger<StashInvalidationInterceptor> _logger;

    public StashInvalidationInterceptor(
        ICacheStore cacheStore,
        ILogger<StashInvalidationInterceptor> logger)
    {
        _cacheStore = cacheStore;
        _logger = logger;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return result;

        var changedTableNames = GetChangedTableNames(eventData.Context);

        if (changedTableNames.Count > 0)
        {
            _logger.LogDebug(StashEventIds.CacheInvalidation,
                "Invalidating cache for tables: [{Tables}]",
                string.Join(", ", changedTableNames));

            await _cacheStore.InvalidateByTagsAsync(changedTableNames, cancellationToken);
        }

        return result;
    }

    private static List<string> GetChangedTableNames(DbContext context)
    {
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var entityType = context.Model.FindEntityType(entry.Entity.GetType());
                var tableName = entityType?.GetTableName();

                if (tableName is not null)
                    tableNames.Add(tableName);
            }
        }

        return [.. tableNames];
    }
}
