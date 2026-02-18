using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Data;
using Stash.EFCore.Extensions;
using Stash.EFCore.Logging;

namespace Stash.EFCore.Interceptors;

/// <summary>
/// Intercepts EF Core database commands to serve cached results on cache hit
/// and store results on cache miss. Supports both reader and scalar queries,
/// per-query TTL via query tags, and automatic table dependency tracking.
/// </summary>
public class StashCommandInterceptor : DbCommandInterceptor
{
    private readonly ICacheStore _cacheStore;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly StashOptions _options;
    private readonly ILogger<StashCommandInterceptor> _logger;

    /// <summary>
    /// Passes cache keys from Executing → Executed. Uses ConditionalWeakTable
    /// so entries are automatically cleaned up when the DbCommand is garbage collected.
    /// Note: AsyncLocal cannot be used here because the async state machine restores
    /// the execution context after MoveNext completes, rolling back AsyncLocal changes.
    /// </summary>
    private static readonly ConditionalWeakTable<DbCommand, string> PendingCacheKeys = new();

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

    #region Reader interception — async

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldCache(command, result.HasResult))
            return result;

        var cacheKey = _keyGenerator.GenerateKey(command);

        try
        {
            var cached = await _cacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                _logger.LogDebug(StashEventIds.CacheHit, "Cache hit for key {CacheKey}", cacheKey);
                return InterceptionResult<DbDataReader>.SuppressWithResult(new CachedDataReader(cached));
            }
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache read failed for key {CacheKey}, falling back to database", cacheKey);
            return result;
        }

        _logger.LogDebug(StashEventIds.CacheMiss, "Cache miss for key {CacheKey}", cacheKey);
        StoreKeyForExecution(command, cacheKey);
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (result is CachedDataReader)
            return result;

        var cacheKey = RetrieveKeyForExecution(command);
        if (cacheKey is null)
            return result;

        var (absoluteTtl, slidingTtl) = ResolveTtl(command);

        // Capture all rows so we can return them to EF Core regardless of caching decision.
        var captured = await CacheableResultSet.CaptureAsync(result, int.MaxValue, cancellationToken);
        if (captured is null)
            return new CachedDataReader(new CacheableResultSet());

        await TryCacheResultSetAsync(cacheKey, captured, command.CommandText, absoluteTtl, slidingTtl, cancellationToken);
        return new CachedDataReader(captured);
    }

    #endregion

    #region Reader interception — sync

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        if (!ShouldCache(command, result.HasResult))
            return result;

        var cacheKey = _keyGenerator.GenerateKey(command);

        try
        {
            var cached = _cacheStore.GetAsync(cacheKey).GetAwaiter().GetResult();
            if (cached is not null)
            {
                _logger.LogDebug(StashEventIds.CacheHit, "Cache hit for key {CacheKey}", cacheKey);
                return InterceptionResult<DbDataReader>.SuppressWithResult(new CachedDataReader(cached));
            }
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache read failed for key {CacheKey}, falling back to database", cacheKey);
            return result;
        }

        _logger.LogDebug(StashEventIds.CacheMiss, "Cache miss for key {CacheKey}", cacheKey);
        StoreKeyForExecution(command, cacheKey);
        return result;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        if (result is CachedDataReader)
            return result;

        var cacheKey = RetrieveKeyForExecution(command);
        if (cacheKey is null)
            return result;

        var (absoluteTtl, slidingTtl) = ResolveTtl(command);
        var captured = CacheableResultSet.CaptureAsync(result, int.MaxValue).GetAwaiter().GetResult();
        if (captured is null)
            return new CachedDataReader(new CacheableResultSet());

        TryCacheResultSetAsync(cacheKey, captured, command.CommandText, absoluteTtl, slidingTtl)
            .GetAwaiter().GetResult();
        return new CachedDataReader(captured);
    }

    #endregion

    #region Scalar interception — async

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldCache(command, result.HasResult))
            return result;

        var cacheKey = _keyGenerator.GenerateKey(command);

        try
        {
            var cached = await _cacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                _logger.LogDebug(StashEventIds.CacheHit, "Cache hit for scalar key {CacheKey}", cacheKey);
                var scalarValue = ExtractScalarValue(cached) ?? DBNull.Value;
                return InterceptionResult<object>.SuppressWithResult(scalarValue);
            }
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache read failed for scalar key {CacheKey}, falling back to database", cacheKey);
            return result;
        }

        _logger.LogDebug(StashEventIds.CacheMiss, "Cache miss for scalar key {CacheKey}", cacheKey);
        StoreKeyForExecution(command, cacheKey);
        return result;
    }

    public override async ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = RetrieveKeyForExecution(command);
        if (cacheKey is null)
            return result;

        var (absoluteTtl, slidingTtl) = ResolveTtl(command);
        var captured = CreateScalarResultSet(result);
        var tables = _keyGenerator.ExtractTableDependencies(command.CommandText);

        try
        {
            await _cacheStore.SetAsync(cacheKey, captured, absoluteTtl, slidingTtl, tables, cancellationToken);
            _logger.LogDebug(StashEventIds.CacheStore,
                "Cached scalar result for key {CacheKey}, tables: [{Tables}]",
                cacheKey, string.Join(", ", tables));
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache write failed for scalar key {CacheKey}", cacheKey);
        }

        return result;
    }

    #endregion

    #region Scalar interception — sync

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        if (!ShouldCache(command, result.HasResult))
            return result;

        var cacheKey = _keyGenerator.GenerateKey(command);

        try
        {
            var cached = _cacheStore.GetAsync(cacheKey).GetAwaiter().GetResult();
            if (cached is not null)
            {
                _logger.LogDebug(StashEventIds.CacheHit, "Cache hit for scalar key {CacheKey}", cacheKey);
                var scalarValue = ExtractScalarValue(cached) ?? DBNull.Value;
                return InterceptionResult<object>.SuppressWithResult(scalarValue);
            }
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache read failed for scalar key {CacheKey}, falling back to database", cacheKey);
            return result;
        }

        _logger.LogDebug(StashEventIds.CacheMiss, "Cache miss for scalar key {CacheKey}", cacheKey);
        StoreKeyForExecution(command, cacheKey);
        return result;
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        var cacheKey = RetrieveKeyForExecution(command);
        if (cacheKey is null)
            return result;

        var (absoluteTtl, slidingTtl) = ResolveTtl(command);
        var captured = CreateScalarResultSet(result);
        var tables = _keyGenerator.ExtractTableDependencies(command.CommandText);

        try
        {
            _cacheStore.SetAsync(cacheKey, captured, absoluteTtl, slidingTtl, tables)
                .GetAwaiter().GetResult();
            _logger.LogDebug(StashEventIds.CacheStore,
                "Cached scalar result for key {CacheKey}, tables: [{Tables}]",
                cacheKey, string.Join(", ", tables));
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache write failed for scalar key {CacheKey}", cacheKey);
        }

        return result;
    }

    #endregion

    #region ShouldCache

    /// <summary>
    /// Determines whether a command should be cached.
    /// </summary>
    internal bool ShouldCache(DbCommand command, bool hasResult)
    {
        // Already handled by another interceptor
        if (hasResult)
            return false;

        var sql = command.CommandText;

        // Explicit NoCache tag
        if (StashTagParser.IsExplicitlyNotCached(sql))
            return false;

        // Non-SELECT command (defensive check — ReaderExecuting is normally only called for SELECTs)
        if (!IsReadCommand(sql))
            return false;

        // Explicit Stash tag (opt-in always works, regardless of CacheAllQueries)
        if (StashTagParser.IsCacheable(sql))
            return true;

        // CacheAllQueries mode
        if (_options.CacheAllQueries)
        {
            if (_options.ExcludedTables.Count > 0)
            {
                var tables = _keyGenerator.ExtractTableDependencies(sql);
                if (tables.Any(t => _options.ExcludedTables.Contains(t)))
                    return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the command is a read (SELECT/WITH) command by skipping leading SQL comments.
    /// </summary>
    internal static bool IsReadCommand(string sql)
    {
        var span = sql.AsSpan().TrimStart();

        while (span.Length > 0)
        {
            if (span.StartsWith("--"))
            {
                var newline = span.IndexOf('\n');
                span = newline < 0 ? [] : span[(newline + 1)..].TrimStart();
            }
            else if (span.StartsWith("/*"))
            {
                var end = span.IndexOf("*/");
                span = end < 0 ? [] : span[(end + 2)..].TrimStart();
            }
            else
            {
                break;
            }
        }

        return span.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
               span.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region TTL resolution

    /// <summary>
    /// Resolves the TTL for a query by parsing the Stash tag, falling back to profile settings,
    /// then to global defaults.
    /// </summary>
    internal (TimeSpan absoluteTtl, TimeSpan? slidingTtl) ResolveTtl(DbCommand command)
    {
        var (absoluteTtl, slidingTtl, profileName) = StashTagParser.ParseCacheTag(command.CommandText);

        // Check for named profile
        if (profileName is not null &&
            _options.Profiles.TryGetValue(profileName, out var profile))
        {
            return (
                profile.AbsoluteExpiration ?? _options.DefaultAbsoluteExpiration,
                profile.SlidingExpiration ?? _options.DefaultSlidingExpiration
            );
        }

        return (
            absoluteTtl ?? _options.DefaultAbsoluteExpiration,
            slidingTtl ?? _options.DefaultSlidingExpiration
        );
    }

    #endregion

    #region Cache key passing

    private static void StoreKeyForExecution(DbCommand command, string cacheKey)
    {
        PendingCacheKeys.AddOrUpdate(command, cacheKey);
    }

    private static string? RetrieveKeyForExecution(DbCommand command)
    {
        if (PendingCacheKeys.TryGetValue(command, out var key))
        {
            PendingCacheKeys.Remove(command);
            return key;
        }

        return null;
    }

    #endregion

    #region Cache storage

    /// <summary>
    /// Attempts to cache a result set, respecting MaxRowsPerQuery and MaxCacheEntrySize limits.
    /// </summary>
    private async Task TryCacheResultSetAsync(
        string cacheKey,
        CacheableResultSet captured,
        string commandText,
        TimeSpan absoluteTtl,
        TimeSpan? slidingTtl,
        CancellationToken cancellationToken = default)
    {
        if (captured.Rows.Length > _options.MaxRowsPerQuery)
        {
            _logger.LogDebug(StashEventIds.SkippedTooManyRows,
                "Skipping cache for key {CacheKey}: {RowCount} rows exceeds limit of {MaxRows}",
                cacheKey, captured.Rows.Length, _options.MaxRowsPerQuery);
            return;
        }

        if (_options.MaxCacheEntrySize > 0 && captured.ApproximateSizeBytes > _options.MaxCacheEntrySize)
        {
            _logger.LogDebug(StashEventIds.SkippedTooLarge,
                "Skipping cache for key {CacheKey}: {Size} bytes exceeds limit of {MaxSize}",
                cacheKey, captured.ApproximateSizeBytes, _options.MaxCacheEntrySize);
            return;
        }

        var tables = _keyGenerator.ExtractTableDependencies(commandText);

        try
        {
            await _cacheStore.SetAsync(cacheKey, captured, absoluteTtl, slidingTtl, tables, cancellationToken);

            _logger.LogDebug(StashEventIds.CacheStore,
                "Cached {RowCount} rows for key {CacheKey}, tables: [{Tables}]",
                captured.Rows.Length, cacheKey, string.Join(", ", tables));
        }
        catch (Exception ex) when (_options.FallbackToDatabase)
        {
            _logger.LogWarning(StashEventIds.CacheError, ex,
                "Cache write failed for key {CacheKey}", cacheKey);
        }
    }

    #endregion

    #region Scalar helpers

    private static CacheableResultSet CreateScalarResultSet(object? value)
    {
        return new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition
                {
                    Name = "Scalar",
                    Ordinal = 0,
                    DataTypeName = "object",
                    FieldType = value?.GetType() ?? typeof(object)
                }
            ],
            Rows = [new[] { value is DBNull ? null : value }],
            ApproximateSizeBytes = CacheableResultSet.EstimateValueSize(value) + 64,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static object? ExtractScalarValue(CacheableResultSet resultSet)
    {
        if (resultSet.Rows.Length == 0 || resultSet.Rows[0].Length == 0)
            return null;

        return resultSet.Rows[0][0];
    }

    #endregion
}
