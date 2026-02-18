using System.Data.Common;

namespace Stash.EFCore.Caching;

/// <summary>
/// Generates deterministic cache keys from database commands.
/// </summary>
public interface ICacheKeyGenerator
{
    /// <summary>
    /// Generates a cache key from the SQL command text and parameters.
    /// </summary>
    string GenerateKey(DbCommand command);
}
