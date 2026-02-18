using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Interceptors;

namespace Stash.EFCore.Extensions;

/// <summary>
/// Extension methods for adding Stash interceptors to a <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds Stash caching and invalidation interceptors to the DbContext configuration.
    /// Typically called from <c>OnConfiguring</c> or <c>AddDbContext</c> when not using
    /// <see cref="Configuration.ServiceCollectionExtensions.AddStash"/>.
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
