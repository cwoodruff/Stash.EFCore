using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Extensions;

/// <summary>
/// Extension methods for adding Stash interceptors to a <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds Stash caching and invalidation interceptors to the DbContext configuration.
    /// Resolves the interceptors from the <paramref name="serviceProvider"/>.
    /// Use inside <c>AddDbContext</c> with the service-provider-aware overload.
    /// </summary>
    public static DbContextOptionsBuilder UseStash(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        var commandInterceptor = serviceProvider.GetRequiredService<StashCommandInterceptor>();
        var invalidationInterceptor = serviceProvider.GetRequiredService<StashInvalidationInterceptor>();
        builder.AddInterceptors(commandInterceptor, invalidationInterceptor);
        return builder;
    }

    /// <summary>
    /// Adds Stash caching and invalidation interceptors to the DbContext configuration.
    /// Use when you already have references to the interceptor instances.
    /// </summary>
    public static DbContextOptionsBuilder UseStash(
        this DbContextOptionsBuilder builder,
        StashCommandInterceptor commandInterceptor,
        StashInvalidationInterceptor invalidationInterceptor)
    {
        builder.AddInterceptors(commandInterceptor, invalidationInterceptor);
        return builder;
    }
}
