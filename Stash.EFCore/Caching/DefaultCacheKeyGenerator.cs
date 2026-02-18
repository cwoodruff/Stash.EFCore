using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Stash.EFCore.Configuration;
using Stash.EFCore.Data;

namespace Stash.EFCore.Caching;

/// <summary>
/// Default cache key generator that produces a SHA256 hash of SQL + parameters,
/// and delegates table extraction to <see cref="TableDependencyParser"/>.
/// </summary>
public class DefaultCacheKeyGenerator : ICacheKeyGenerator
{
    private readonly StashOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultCacheKeyGenerator"/> with the specified options.
    /// </summary>
    /// <param name="options">The Stash configuration options, used for the <see cref="StashOptions.KeyPrefix"/>.</param>
    public DefaultCacheKeyGenerator(StashOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string GenerateKey(DbCommand command)
    {
        var sb = new StringBuilder();
        sb.Append(command.CommandText);

        foreach (DbParameter parameter in command.Parameters)
        {
            sb.Append('|');
            sb.Append(parameter.ParameterName);
            sb.Append('=');
            sb.Append(parameter.Value ?? "NULL");
            sb.Append(':');
            sb.Append(parameter.DbType);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
#if NET9_0_OR_GREATER
        return $"{_options.KeyPrefix}{Convert.ToHexStringLower(hash)}";
#else
        return $"{_options.KeyPrefix}{Convert.ToHexString(hash).ToLowerInvariant()}";
#endif
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> ExtractTableDependencies(string commandText)
    {
        return TableDependencyParser.ExtractTableNames(commandText);
    }
}
