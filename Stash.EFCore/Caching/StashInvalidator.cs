using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Configuration;
using Stash.EFCore.Diagnostics;
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
    private readonly StashOptions _options;
    private readonly StashStatistics? _statistics;

    public StashInvalidator(
        ICacheStore cacheStore,
        ILogger<StashInvalidator> logger,
        StashOptions options,
        IStashStatistics? statistics = null)
    {
        _cacheStore = cacheStore;
        _logger = logger;
        _options = options;
        _statistics = statistics as StashStatistics;
    }

    public async Task InvalidateTablesAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        var tables = tableNames as IReadOnlyList<string> ?? tableNames.ToList();

        _statistics?.RecordInvalidation(tables);
        _logger.LogDebug(StashEventIds.CacheInvalidated,
            "Manual invalidation for tables: [{Tables}]",
            string.Join(", ", tables));

        _options.OnStashEvent?.Invoke(new StashEvent
        {
            EventType = StashEventType.CacheInvalidated,
            Tables = tables
        });

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
            var tables = tableNames.ToList();
            _statistics?.RecordInvalidation(tables);
            _logger.LogDebug(StashEventIds.CacheInvalidated,
                "Manual entity invalidation for tables: [{Tables}]",
                string.Join(", ", tables));

            _options.OnStashEvent?.Invoke(new StashEvent
            {
                EventType = StashEventType.CacheInvalidated,
                Tables = tables
            });

            await _cacheStore.InvalidateByTagsAsync(tables, cancellationToken);
        }
    }

    public async Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        _statistics?.RecordInvalidation([]);
        _logger.LogDebug(StashEventIds.CacheInvalidated,
            "Manual invalidation for key: {Key}", cacheKey);

        _options.OnStashEvent?.Invoke(new StashEvent
        {
            EventType = StashEventType.CacheInvalidated,
            CacheKey = cacheKey
        });

        await _cacheStore.InvalidateKeyAsync(cacheKey, cancellationToken);
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        _statistics?.RecordInvalidation([]);
        _logger.LogDebug(StashEventIds.CacheInvalidated, "Manual invalidation of all cache entries");

        _options.OnStashEvent?.Invoke(new StashEvent
        {
            EventType = StashEventType.CacheInvalidated
        });

        await _cacheStore.InvalidateAllAsync(cancellationToken);
    }
}
