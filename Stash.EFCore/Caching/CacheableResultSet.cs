using System.Data.Common;

namespace Stash.EFCore.Caching;

/// <summary>
/// A serializable representation of a DbDataReader result set for cache storage.
/// </summary>
public class CacheableResultSet
{
    public required ColumnInfo[] Columns { get; set; }
    public required List<object?[]> Rows { get; set; }

    /// <summary>
    /// Table names this result set depends on, used for tag-based invalidation.
    /// </summary>
    public required string[] TableDependencies { get; set; }

    /// <summary>
    /// Reads all rows and schema from a <see cref="DbDataReader"/> into a cacheable structure.
    /// The source reader is fully consumed and should not be used afterward.
    /// </summary>
    public static async Task<CacheableResultSet> FromDataReaderAsync(
        DbDataReader reader, string[] tableDependencies, CancellationToken cancellationToken = default)
    {
        var columns = new ColumnInfo[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns[i] = new ColumnInfo
            {
                Name = reader.GetName(i),
                DataTypeName = reader.GetDataTypeName(i),
                FieldType = reader.GetFieldType(i),
                Ordinal = i
            };
        }

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }

        return new CacheableResultSet
        {
            Columns = columns,
            Rows = rows,
            TableDependencies = tableDependencies
        };
    }

    public class ColumnInfo
    {
        public required string Name { get; set; }
        public required string DataTypeName { get; set; }
        public required Type FieldType { get; set; }
        public required int Ordinal { get; set; }
    }
}
