using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;

namespace Stash.EFCore.Diagnostics;

/// <summary>
/// Health check that reports cache health based on statistics.
/// <list type="bullet">
/// <item><b>Healthy</b> — Cache is reachable and hit rate is at or above <see cref="StashOptions.MinimumHitRatePercent"/>.</item>
/// <item><b>Degraded</b> — Cache is reachable but hit rate is below threshold.</item>
/// <item><b>Unhealthy</b> — Cache store is unreachable (GetAsync throws).</item>
/// </list>
/// </summary>
public class StashHealthCheck : IHealthCheck
{
    private readonly ICacheStore _cacheStore;
    private readonly IStashStatistics _statistics;
    private readonly StashOptions _options;

    public StashHealthCheck(ICacheStore cacheStore, IStashStatistics statistics, StashOptions options)
    {
        _cacheStore = cacheStore;
        _statistics = statistics;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Probe cache connectivity with a known-missing key
            await _cacheStore.GetAsync("stash:health-check-probe", cancellationToken);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Cache store is unreachable.",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }

        var data = new Dictionary<string, object>
        {
            ["hits"] = _statistics.Hits,
            ["misses"] = _statistics.Misses,
            ["hitRate"] = _statistics.HitRate,
            ["invalidations"] = _statistics.Invalidations,
            ["errors"] = _statistics.Errors,
            ["totalBytesCached"] = _statistics.TotalBytesCached
        };

        var totalRequests = _statistics.Hits + _statistics.Misses;
        if (totalRequests == 0)
        {
            return HealthCheckResult.Healthy("Cache is reachable. No requests recorded yet.", data);
        }

        if (_statistics.HitRate < _options.MinimumHitRatePercent)
        {
            return HealthCheckResult.Degraded(
                $"Cache hit rate ({_statistics.HitRate:F1}%) is below threshold ({_options.MinimumHitRatePercent}%).",
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"Cache is healthy. Hit rate: {_statistics.HitRate:F1}%.",
            data);
    }
}
