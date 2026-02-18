using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stash.EFCore.Caching;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Stash second-level cache services with the dependency injection container.
    /// </summary>
    public static IServiceCollection AddStash(this IServiceCollection services, Action<StashOptions>? configure = null)
    {
        var options = new StashOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();
        services.TryAddSingleton<ICacheStore, MemoryCacheStore>();
        services.AddSingleton<StashCommandInterceptor>();
        services.AddSingleton<StashInvalidationInterceptor>();

        return services;
    }
}
