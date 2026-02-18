using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stash.EFCore.Caching;
using Stash.EFCore.Diagnostics;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Stash second-level cache services backed by <see cref="MemoryCacheStore"/>.
    /// </summary>
    public static IServiceCollection AddStash(this IServiceCollection services, Action<StashOptions>? configure = null)
    {
        var options = new StashOptions();
        configure?.Invoke(options);

        services.AddMemoryCache();
        services.AddSingleton(options);
        services.TryAddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();
        services.TryAddSingleton<ICacheStore, MemoryCacheStore>();
        services.AddSingleton<StashCommandInterceptor>();
        services.AddSingleton<StashInvalidationInterceptor>();
        services.TryAddSingleton<IStashInvalidator, StashInvalidator>();

        // Diagnostics
        var stats = new StashStatistics();
        services.TryAddSingleton<IStashStatistics>(stats);
        services.TryAddSingleton(stats);

        return services;
    }

#if NET9_0_OR_GREATER
    /// <summary>
    /// Registers Stash second-level cache services backed by <see cref="HybridCacheStore"/>
    /// for L1 (memory) + L2 (distributed) scenarios.
    /// Requires <c>AddHybridCache()</c> to be called separately with an <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> provider.
    /// </summary>
    public static IServiceCollection AddStashWithHybridCache(this IServiceCollection services, Action<StashOptions>? configure = null)
    {
        var options = new StashOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();
        services.TryAddSingleton<ICacheStore, HybridCacheStore>();
        services.AddSingleton<StashCommandInterceptor>();
        services.AddSingleton<StashInvalidationInterceptor>();
        services.TryAddSingleton<IStashInvalidator, StashInvalidator>();

        // Diagnostics
        var stats = new StashStatistics();
        services.TryAddSingleton<IStashStatistics>(stats);
        services.TryAddSingleton(stats);

        return services;
    }
#endif

    /// <summary>
    /// Adds the Stash cache health check to the health checks builder.
    /// </summary>
    public static IHealthChecksBuilder AddStashHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "stash-cache",
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Add(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration(
            name,
            sp => new StashHealthCheck(
                sp.GetRequiredService<ICacheStore>(),
                sp.GetRequiredService<IStashStatistics>(),
                sp.GetRequiredService<StashOptions>()),
            failureStatus,
            tags));

        return builder;
    }
}
