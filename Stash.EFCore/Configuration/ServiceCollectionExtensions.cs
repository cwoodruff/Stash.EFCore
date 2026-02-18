using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stash.EFCore.Caching;
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

        return services;
    }
#endif
}
