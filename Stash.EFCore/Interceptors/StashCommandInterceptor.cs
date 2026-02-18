using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Data;
using Stash.EFCore.Logging;

namespace Stash.EFCore.Interceptors;

/// <summary>
/// Intercepts EF Core database commands to serve cached results on cache hit
/// and store results on cache miss.
/// </summary>
public class StashCommandInterceptor : DbCommandInterceptor
{
    private readonly ICacheStore _cacheStore;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly StashOptions _options;
    private readonly ILogger<StashCommandInterceptor> _logger;

    public StashCommandInterceptor(
        ICacheStore cacheStore,
        ICacheKeyGenerator keyGenerator,
        StashOptions options,
        ILogger<StashCommandInterceptor> logger)
    {
        _cacheStore = cacheStore;
        _keyGenerator = keyGenerator;
        _options = options;
        _logger = logger;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldCache(command))
            return result;

        var cacheKey = _keyGenerator.GenerateKey(command);
        var cached = await _cacheStore.GetAsync(cacheKey, cancellationToken);

        if (cached is not null)
        {
            _logger.LogDebug(StashEventIds.CacheHit, "Cache hit for key {CacheKey}", cacheKey);
            return InterceptionResult<DbDataReader>.SuppressWithResult(new CachedDataReader(cached));
        }

        _logger.LogDebug(StashEventIds.CacheMiss, "Cache miss for key {CacheKey}", cacheKey);
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldCache(command))
            return result;

        var cacheKey = _keyGenerator.GenerateKey(command);

        // Check if this result was served from cache (already suppressed)
        if (result is CachedDataReader)
            return result;

        var tableDependencies = TableDependencyParser.ExtractTableNames(command.CommandText);
        var resultSet = await CacheableResultSet.FromDataReaderAsync(result, tableDependencies, cancellationToken);

        await _cacheStore.SetAsync(
            cacheKey,
            resultSet,
            _options.DefaultAbsoluteExpiration,
            _options.DefaultSlidingExpiration,
            cancellationToken);

        _logger.LogDebug(StashEventIds.CacheStore, "Stored {RowCount} rows for key {CacheKey} with dependencies [{Tables}]",
            resultSet.Rows.Count, cacheKey, string.Join(", ", tableDependencies));

        return new CachedDataReader(resultSet);
    }

    private bool ShouldCache(DbCommand command)
    {
        if (_options.EnableGlobalCaching)
            return true;

        // Check for .Stash() tag marker in the command text
        return command.CommandText.Contains(Extensions.QueryableExtensions.StashTag, StringComparison.Ordinal);
    }
}
