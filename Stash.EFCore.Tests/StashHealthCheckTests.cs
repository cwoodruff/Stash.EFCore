using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Diagnostics;
using Xunit;

namespace Stash.EFCore.Tests;

public class StashHealthCheckTests
{
    private readonly Mock<ICacheStore> _cacheStore;
    private readonly StashStatistics _statistics;
    private readonly StashOptions _options;
    private readonly StashHealthCheck _healthCheck;

    public StashHealthCheckTests()
    {
        _cacheStore = new Mock<ICacheStore>();
        _cacheStore.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CacheableResultSet?)null);

        _statistics = new StashStatistics();
        _options = new StashOptions { MinimumHitRatePercent = 10.0 };
        _healthCheck = new StashHealthCheck(_cacheStore.Object, _statistics, _options);
    }

    [Fact]
    public async Task Healthy_WhenCacheReachableAndHitRateAboveThreshold()
    {
        // 80% hit rate
        for (var i = 0; i < 8; i++) _statistics.RecordHit();
        for (var i = 0; i < 2; i++) _statistics.RecordMiss();

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("hits").WhoseValue.Should().Be(8L);
        result.Data.Should().ContainKey("misses").WhoseValue.Should().Be(2L);
        result.Data.Should().ContainKey("hitRate");
    }

    [Fact]
    public async Task Healthy_WhenNoRequestsYet()
    {
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("No requests recorded");
    }

    [Fact]
    public async Task Degraded_WhenHitRateBelowThreshold()
    {
        // 5% hit rate (below 10% threshold)
        _statistics.RecordHit();
        for (var i = 0; i < 19; i++) _statistics.RecordMiss();

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("below threshold");
    }

    [Fact]
    public async Task Unhealthy_WhenCacheStoreThrows()
    {
        _cacheStore.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("unreachable");
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Degraded_ExactlyAtThreshold_IsHealthy()
    {
        _options.MinimumHitRatePercent = 50.0;

        // Exactly 50% hit rate
        for (var i = 0; i < 5; i++) _statistics.RecordHit();
        for (var i = 0; i < 5; i++) _statistics.RecordMiss();

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task DataDictionary_ContainsAllMetrics()
    {
        _statistics.RecordHit();
        _statistics.RecordMiss();
        _statistics.RecordError();
        _statistics.RecordCachedBytes(1024);
        _statistics.RecordInvalidation(["products"]);

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Data.Should().ContainKey("hits");
        result.Data.Should().ContainKey("misses");
        result.Data.Should().ContainKey("hitRate");
        result.Data.Should().ContainKey("invalidations");
        result.Data.Should().ContainKey("errors");
        result.Data.Should().ContainKey("totalBytesCached");
    }

    [Fact]
    public async Task CustomThreshold_Respected()
    {
        _options.MinimumHitRatePercent = 90.0;

        // 80% hit rate, below 90% threshold
        for (var i = 0; i < 8; i++) _statistics.RecordHit();
        for (var i = 0; i < 2; i++) _statistics.RecordMiss();

        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }
}
