using System.Data;
using System.Data.Common;

namespace Stash.EFCore.Caching;

/// <summary>
/// Column metadata for a cached result set.
/// </summary>
public sealed class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string DataTypeName { get; set; } = string.Empty;
    public Type FieldType { get; set; } = typeof(object);
    public bool AllowDBNull { get; set; }
}

/// <summary>
/// A serializable data structure that captures the complete result of a DbDataReader.
/// </summary>
public sealed class CacheableResultSet
{
    /// <summary>Column definitions (name, ordinal, .NET type, DB type name).</summary>
    public ColumnDefinition[] Columns { get; set; } = [];

    /// <summary>All rows as object arrays. Each array has Columns.Length elements.</summary>
    public object?[][] Rows { get; set; } = [];

    /// <summary>Number of result sets (for queries that return multiple result sets).</summary>
    public int ResultSetCount { get; set; } = 1;

    /// <summary>Records affected count (for non-query commands).</summary>
    public int RecordsAffected { get; set; } = -1;

    /// <summary>Approximate size in bytes for size-limit enforcement.</summary>
    public long ApproximateSizeBytes { get; set; }

    /// <summary>When this result set was captured from the database.</summary>
    public DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>
    /// Captures the result of a live <see cref="DbDataReader"/> into a cacheable structure.
    /// The reader is fully consumed and closed after capture.
    /// Returns null if the result set exceeds <paramref name="maxRows"/>, signaling "do not cache".
    /// </summary>
    public static async Task<CacheableResultSet?> CaptureAsync(
        DbDataReader reader, int maxRows, CancellationToken ct = default)
    {
        var columns = ReadColumnSchema(reader);
        var rows = new List<object?[]>();
        long sizeBytes = 0;

        foreach (var col in columns)
            sizeBytes += EstimateStringSize(col.Name) + EstimateStringSize(col.DataTypeName) + 16;

        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows)
            {
                await reader.CloseAsync();
                return null;
            }

            var values = new object?[columns.Length];
            for (var i = 0; i < columns.Length; i++)
            {
                if (reader.IsDBNull(i))
                {
                    values[i] = null;
                }
                else
                {
                    var value = reader.GetValue(i);
                    values[i] = value is DBNull ? null : value;
                    sizeBytes += EstimateValueSize(values[i]);
                }
            }

            rows.Add(values);
        }

        // Per-row overhead: array reference (8) + array header (16) + per-element reference (8 each)
        sizeBytes += rows.Count * (24L + columns.Length * 8L);

        await reader.CloseAsync();

        return new CacheableResultSet
        {
            Columns = columns,
            Rows = rows.ToArray(),
            ApproximateSizeBytes = sizeBytes,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ColumnDefinition[] ReadColumnSchema(DbDataReader reader)
    {
        try
        {
            return ReadFromGetColumnSchema(reader);
        }
        catch (NotSupportedException)
        {
            return ReadFromSchemaTableOrFallback(reader);
        }
    }

    private static ColumnDefinition[] ReadFromGetColumnSchema(DbDataReader reader)
    {
        var schema = reader.GetColumnSchema();
        var columns = new ColumnDefinition[schema.Count];
        for (var i = 0; i < schema.Count; i++)
        {
            var col = schema[i];
            columns[i] = new ColumnDefinition
            {
                Name = col.ColumnName,
                Ordinal = col.ColumnOrdinal ?? i,
                DataTypeName = col.DataTypeName ?? reader.GetDataTypeName(i),
                FieldType = col.DataType ?? reader.GetFieldType(i),
                AllowDBNull = col.AllowDBNull ?? true
            };
        }

        return columns;
    }

    private static ColumnDefinition[] ReadFromSchemaTableOrFallback(DbDataReader reader)
    {
        var schemaTable = reader.GetSchemaTable();
        if (schemaTable is null)
            return ReadFromFieldCount(reader);

        var result = new ColumnDefinition[schemaTable.Rows.Count];
        for (var i = 0; i < schemaTable.Rows.Count; i++)
        {
            var row = schemaTable.Rows[i];
            result[i] = new ColumnDefinition
            {
                Name = row["ColumnName"] as string ?? string.Empty,
                Ordinal = row["ColumnOrdinal"] is int ord ? ord : i,
                DataTypeName = schemaTable.Columns.Contains("DataTypeName") && row["DataTypeName"] is string dtn
                    ? dtn
                    : reader.GetDataTypeName(i),
                FieldType = row["DataType"] as Type ?? typeof(object),
                AllowDBNull = !schemaTable.Columns.Contains("AllowDBNull") || row["AllowDBNull"] is not false
            };
        }

        return result;
    }

    private static ColumnDefinition[] ReadFromFieldCount(DbDataReader reader)
    {
        var columns = new ColumnDefinition[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns[i] = new ColumnDefinition
            {
                Name = reader.GetName(i),
                Ordinal = i,
                DataTypeName = reader.GetDataTypeName(i),
                FieldType = reader.GetFieldType(i),
                AllowDBNull = true
            };
        }

        return columns;
    }

    internal static long EstimateValueSize(object? value) => value switch
    {
        null => 0,
        string s => EstimateStringSize(s),
        byte[] b => b.Length + 24L,
        bool => 1,
        byte or sbyte => 1,
        char or short or ushort => 2,
        int or uint or float => 4,
        long or ulong or double or DateTime => 8,
        DateTimeOffset or TimeSpan => 12,
        decimal or Guid => 16,
        _ => 16
    };

    private static long EstimateStringSize(string s) =>
        s.Length * 2L + 40; // UTF-16 chars + string object overhead
}
