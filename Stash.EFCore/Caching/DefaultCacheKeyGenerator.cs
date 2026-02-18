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

    public DefaultCacheKeyGenerator(StashOptions options)
    {
        _options = options;
    }

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
        return $"{_options.KeyPrefix}{Convert.ToHexStringLower(hash)}";
    }

    public IReadOnlyCollection<string> ExtractTableDependencies(string commandText)
    {
        return TableDependencyParser.ExtractTableNames(commandText);
    }
}
