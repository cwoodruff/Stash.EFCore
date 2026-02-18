using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stash.EFCore.Caching;
using Stash.EFCore.Diagnostics;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Configuration;

/// <summary>
/// Extension methods for registering Stash second-level cache services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Stash second-level cache services backed by <see cref="MemoryCacheStore"/>.
    /// Call <see cref="Extensions.DbContextOptionsBuilderExtensions.UseStash(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, IServiceProvider)"/>
    /// on your DbContext to activate the interceptors.
    /// </summary>
    /// <param name="services">The service collection to register Stash services in.</param>
    /// <param name="configure">Optional callback to configure <see cref="StashOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddStash(options =>
    /// {
    ///     options.DefaultAbsoluteExpiration = TimeSpan.FromMinutes(10);
    ///     options.CacheAllQueries = true;
    /// });
    /// </code>
    /// </example>
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
    /// <param name="services">The service collection to register Stash services in.</param>
    /// <param name="configure">Optional callback to configure <see cref="StashOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddStackExchangeRedisCache(o => o.Configuration = "localhost");
    /// services.AddHybridCache();
    /// services.AddStashWithHybridCache();
    /// </code>
    /// </example>
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
    /// Reports <b>Healthy</b> when the cache is reachable and the hit rate meets the configured threshold,
    /// <b>Degraded</b> when hit rate is below threshold, and <b>Unhealthy</b> when the cache store is unreachable.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name.</param>
    /// <param name="failureStatus">The failure status to report when the check fails.</param>
    /// <param name="tags">Optional tags for filtering health checks.</param>
    /// <returns>The health checks builder for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddHealthChecks().AddStashHealthCheck();
    /// </code>
    /// </example>
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
