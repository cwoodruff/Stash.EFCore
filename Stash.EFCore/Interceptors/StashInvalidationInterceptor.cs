using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Diagnostics;
using Stash.EFCore.Logging;

namespace Stash.EFCore.Interceptors;

/// <summary>
/// Intercepts SaveChanges to automatically invalidate cached queries
/// whose table dependencies overlap with the modified entity types.
/// Table names are captured in SavingChanges (before save, while entries still have
/// their pre-save states) and invalidation occurs in SavedChanges (after save succeeds).
/// On failure, captured table names are discarded without invalidation.
/// </summary>
public class StashInvalidationInterceptor : SaveChangesInterceptor
{
    private readonly ICacheStore _cacheStore;
    private readonly ILogger<StashInvalidationInterceptor> _logger;
    private readonly StashOptions _options;
    private readonly StashStatistics? _statistics;

    /// <summary>
    /// Stores table names captured before save, keyed by DbContext instance.
    /// Using ConditionalWeakTable so entries are auto-cleaned when the DbContext is GC'd.
    /// </summary>
    private static readonly ConditionalWeakTable<DbContext, List<string>> PendingInvalidations = new();

    /// <summary>
    /// Initializes a new instance of <see cref="StashInvalidationInterceptor"/>.
    /// </summary>
    /// <param name="cacheStore">The cache store to invalidate entries in.</param>
    /// <param name="logger">Logger for invalidation events.</param>
    /// <param name="options">Global Stash configuration options.</param>
    /// <param name="statistics">Optional statistics tracker for recording invalidation counts.</param>
    public StashInvalidationInterceptor(
        ICacheStore cacheStore,
        ILogger<StashInvalidationInterceptor> logger,
        StashOptions options,
        IStashStatistics? statistics = null)
    {
        _cacheStore = cacheStore;
        _logger = logger;
        _options = options;
        _statistics = statistics as StashStatistics;
    }

    #region Sync overrides

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CaptureChangedTables(eventData.Context);
        return result;
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        InvalidateCapturedTables(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override void SaveChangesFailed(
        DbContextErrorEventData eventData)
    {
        DiscardCapturedTables(eventData.Context);
    }

    #endregion

    #region Async overrides

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureChangedTables(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await InvalidateCapturedTables(eventData.Context, cancellationToken);
        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        DiscardCapturedTables(eventData.Context);
        return Task.CompletedTask;
    }

    #endregion

    private static void CaptureChangedTables(DbContext? context)
    {
        if (context is null)
            return;

        var tableNames = GetChangedTableNames(context);
        if (tableNames.Count > 0)
            PendingInvalidations.AddOrUpdate(context, tableNames);
    }

    private static void DiscardCapturedTables(DbContext? context)
    {
        if (context is not null)
            PendingInvalidations.Remove(context);
    }

    private async Task InvalidateCapturedTables(DbContext? context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            return;

        if (PendingInvalidations.TryGetValue(context, out var tableNames))
        {
            PendingInvalidations.Remove(context);

            _statistics?.RecordInvalidation(tableNames);
            _logger.LogDebug(StashEventIds.CacheInvalidated,
                "Invalidating cache for tables: [{Tables}]",
                string.Join(", ", tableNames));

            _options.OnStashEvent?.Invoke(new StashEvent
            {
                EventType = StashEventType.CacheInvalidated,
                Tables = tableNames
            });

            await _cacheStore.InvalidateByTagsAsync(tableNames, cancellationToken);
        }
    }

    /// <summary>
    /// Extracts the set of table names affected by tracked entity changes.
    /// Navigates owned entities to include their table names as well.
    /// All table names are lowercased for consistent tag matching.
    /// </summary>
    internal static List<string> GetChangedTableNames(DbContext context)
    {
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var entityType = context.Model.FindEntityType(entry.Entity.GetType());
                if (entityType is null)
                    continue;

                var tableName = entityType.GetTableName();
                if (tableName is not null)
                    tableNames.Add(tableName.ToLowerInvariant());

                // Include tables for owned entity navigations
                foreach (var navigation in entityType.GetNavigations())
                {
                    if (navigation.TargetEntityType.IsOwned())
                    {
                        var ownedTableName = navigation.TargetEntityType.GetTableName();
                        if (ownedTableName is not null)
                            tableNames.Add(ownedTableName.ToLowerInvariant());
                    }
                }
            }
        }

        return [.. tableNames];
    }
}
