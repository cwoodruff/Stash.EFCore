using System.Data.Common;

namespace Stash.EFCore.Caching;

/// <summary>
/// Generates deterministic cache keys from database commands and extracts table dependencies.
/// </summary>
public interface ICacheKeyGenerator
{
    /// <summary>
    /// Generates a deterministic cache key from the SQL command text and parameters.
    /// </summary>
    /// <param name="command">The database command to generate a key for.</param>
    /// <returns>A cache key string, typically prefixed with <see cref="Configuration.StashOptions.KeyPrefix"/>.</returns>
    string GenerateKey(DbCommand command);

    /// <summary>
    /// Extracts table names referenced in the SQL command text for cache invalidation tagging.
    /// </summary>
    /// <param name="commandText">The SQL command text to parse.</param>
    /// <returns>Unique table names found in FROM and JOIN clauses.</returns>
    IReadOnlyCollection<string> ExtractTableDependencies(string commandText);
}
