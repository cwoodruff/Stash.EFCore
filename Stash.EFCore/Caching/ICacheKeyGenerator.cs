using System.Data.Common;

namespace Stash.EFCore.Caching;

/// <summary>
/// Generates deterministic cache keys from database commands and extracts table dependencies.
/// </summary>
public interface ICacheKeyGenerator
{
    /// <summary>
    /// Generates a cache key from the SQL command text and parameters.
    /// </summary>
    string GenerateKey(DbCommand command);

    /// <summary>
    /// Extracts table names referenced in the SQL command text for cache invalidation tagging.
    /// </summary>
    IReadOnlyCollection<string> ExtractTableDependencies(string commandText);
}
