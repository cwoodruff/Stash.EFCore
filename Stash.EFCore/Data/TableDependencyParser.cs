using System.Text.RegularExpressions;

namespace Stash.EFCore.Data;

/// <summary>
/// Extracts table names from SQL command text by parsing FROM and JOIN clauses.
/// </summary>
public static partial class TableDependencyParser
{
    // Matches table names after FROM and JOIN keywords, handling optional schema prefix and quoting.
    // Examples: FROM "Products", JOIN [dbo].[Orders], FROM Products AS p
    [GeneratedRegex(
        @"(?:FROM|JOIN)\s+(?:\[?(\w+)\]?\.)?\[?""?(\w+)""?\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TableNamePattern();

    /// <summary>
    /// Extracts unique table names from SQL text.
    /// </summary>
    public static string[] ExtractTableNames(string sql)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in TableNamePattern().Matches(sql))
        {
            // Group 2 is the table name; Group 1 is the optional schema
            var tableName = match.Groups[2].Value;
            tables.Add(tableName);
        }

        return [.. tables];
    }
}
