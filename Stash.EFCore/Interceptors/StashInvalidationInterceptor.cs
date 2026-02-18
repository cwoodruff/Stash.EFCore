using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Caching;
using Stash.EFCore.Logging;

namespace Stash.EFCore.Interceptors;

/// <summary>
/// Intercepts SaveChanges to automatically invalidate cached queries
/// whose table dependencies overlap with the modified entity types.
/// Table names are captured in SavingChanges (before save, while entries still have
/// their pre-save states) and invalidation occurs in SavedChanges (after save succeeds).
/// </summary>
public class StashInvalidationInterceptor : SaveChangesInterceptor
{
    private readonly ICacheStore _cacheStore;
    private readonly ILogger<StashInvalidationInterceptor> _logger;

    /// <summary>
    /// Stores table names captured before save, keyed by DbContext instance.
    /// </summary>
    private static readonly ConditionalWeakTable<DbContext, List<string>> PendingInvalidations = new();

    public StashInvalidationInterceptor(
        ICacheStore cacheStore,
        ILogger<StashInvalidationInterceptor> logger)
    {
        _cacheStore = cacheStore;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CaptureChangedTables(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureChangedTables(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        InvalidateCapturedTables(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await InvalidateCapturedTables(eventData.Context, cancellationToken);
        return result;
    }

    private static void CaptureChangedTables(DbContext? context)
    {
        if (context is null)
            return;

        var tableNames = GetChangedTableNames(context);
        if (tableNames.Count > 0)
            PendingInvalidations.AddOrUpdate(context, tableNames);
    }

    private async Task InvalidateCapturedTables(DbContext? context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            return;

        if (PendingInvalidations.TryGetValue(context, out var tableNames))
        {
            PendingInvalidations.Remove(context);

            _logger.LogDebug(StashEventIds.CacheInvalidation,
                "Invalidating cache for tables: [{Tables}]",
                string.Join(", ", tableNames));

            await _cacheStore.InvalidateByTagsAsync(tableNames, cancellationToken);
        }
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
