using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.EFCore.Caching;

/// <summary>
/// Serializes and deserializes <see cref="CacheableResultSet"/> instances to/from byte arrays
/// using System.Text.Json. Includes a restricted <see cref="JsonConverter{Type}"/> that only
/// allows types from the System namespace to prevent arbitrary type instantiation.
/// </summary>
public static class CacheableResultSetSerializer
{
    /// <summary>
    /// Serializes a <see cref="CacheableResultSet"/> to a UTF-8 JSON byte array.
    /// </summary>
    public static byte[] Serialize(CacheableResultSet resultSet)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        WriteColumns(writer, resultSet.Columns);
        WriteRows(writer, resultSet.Rows, resultSet.Columns);

        writer.WriteNumber("ResultSetCount", resultSet.ResultSetCount);
        writer.WriteNumber("RecordsAffected", resultSet.RecordsAffected);
        writer.WriteNumber("ApproximateSizeBytes", resultSet.ApproximateSizeBytes);
        writer.WriteString("CapturedAtUtc", resultSet.CapturedAtUtc);

        writer.WriteEndObject();
        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>
    /// Deserializes a <see cref="CacheableResultSet"/> from a UTF-8 JSON byte array.
    /// Returns null on any deserialization error (corruption, invalid types, malformed JSON).
    /// </summary>
    public static CacheableResultSet? Deserialize(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            var columns = DeserializeColumns(root.GetProperty("Columns"));
            var rows = DeserializeRows(root.GetProperty("Rows"), columns);

            return new CacheableResultSet
            {
                Columns = columns,
                Rows = rows,
                ResultSetCount = root.TryGetProperty("ResultSetCount", out var rsc) ? rsc.GetInt32() : 1,
                RecordsAffected = root.TryGetProperty("RecordsAffected", out var ra) ? ra.GetInt32() : -1,
                ApproximateSizeBytes = root.TryGetProperty("ApproximateSizeBytes", out var asb) ? asb.GetInt64() : 0,
                CapturedAtUtc = root.TryGetProperty("CapturedAtUtc", out var cau) ? cau.GetDateTimeOffset() : default
            };
        }
        catch
        {
            return null;
        }
    }

    private static void WriteColumns(Utf8JsonWriter writer, ColumnDefinition[] columns)
    {
        writer.WritePropertyName("Columns");
        writer.WriteStartArray();

        foreach (var col in columns)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", col.Name);
            writer.WriteNumber("Ordinal", col.Ordinal);
            writer.WriteString("DataTypeName", col.DataTypeName);
            writer.WriteString("FieldType", SafeTypeConverter.TypeToString(col.FieldType));
            writer.WriteBoolean("AllowDBNull", col.AllowDBNull);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRows(Utf8JsonWriter writer, object?[][] rows, ColumnDefinition[] columns)
    {
        writer.WritePropertyName("Rows");
        writer.WriteStartArray();

        foreach (var row in rows)
        {
            writer.WriteStartArray();
            for (var i = 0; i < row.Length; i++)
                WriteValue(writer, row[i]);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        // Use JsonSerializer to write the value based on its runtime type.
        // This correctly handles: int, long, string, bool, DateTime, DateTimeOffset,
        // decimal, double, float, Guid, byte[] (base64), char (string), TimeSpan, etc.
        JsonSerializer.Serialize(writer, value, value.GetType());
    }

    private static ColumnDefinition[] DeserializeColumns(JsonElement columnsElement)
    {
        var columns = new ColumnDefinition[columnsElement.GetArrayLength()];
        var i = 0;
        foreach (var col in columnsElement.EnumerateArray())
        {
            columns[i++] = new ColumnDefinition
            {
                Name = col.GetProperty("Name").GetString()!,
                Ordinal = col.GetProperty("Ordinal").GetInt32(),
                DataTypeName = col.GetProperty("DataTypeName").GetString()!,
                FieldType = SafeTypeConverter.StringToType(col.GetProperty("FieldType").GetString()!),
                AllowDBNull = col.GetProperty("AllowDBNull").GetBoolean()
            };
        }

        return columns;
    }

    private static object?[][] DeserializeRows(JsonElement rowsElement, ColumnDefinition[] columns)
    {
        var rows = new object?[rowsElement.GetArrayLength()][];
        var rowIdx = 0;
        foreach (var row in rowsElement.EnumerateArray())
        {
            var values = new object?[columns.Length];
            var colIdx = 0;
            foreach (var cell in row.EnumerateArray())
            {
                values[colIdx] = DeserializeValue(cell, columns[colIdx].FieldType);
                colIdx++;
            }

            rows[rowIdx++] = values;
        }

        return rows;
    }

    private static object? DeserializeValue(JsonElement element, Type fieldType)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (fieldType == typeof(bool)) return element.GetBoolean();
        if (fieldType == typeof(byte)) return element.GetByte();
        if (fieldType == typeof(sbyte)) return element.GetSByte();
        if (fieldType == typeof(byte[])) return element.GetBytesFromBase64();
        if (fieldType == typeof(char))
        {
            var s = element.GetString();
            return s is { Length: > 0 } ? s[0] : '\0';
        }
        if (fieldType == typeof(short)) return element.GetInt16();
        if (fieldType == typeof(ushort)) return element.GetUInt16();
        if (fieldType == typeof(int)) return element.GetInt32();
        if (fieldType == typeof(uint)) return element.GetUInt32();
        if (fieldType == typeof(long)) return element.GetInt64();
        if (fieldType == typeof(ulong)) return element.GetUInt64();
        if (fieldType == typeof(float)) return element.GetSingle();
        if (fieldType == typeof(double)) return element.GetDouble();
        if (fieldType == typeof(decimal)) return element.GetDecimal();
        if (fieldType == typeof(string)) return element.GetString();
        if (fieldType == typeof(DateTime)) return element.GetDateTime();
        if (fieldType == typeof(DateTimeOffset)) return element.GetDateTimeOffset();
        if (fieldType == typeof(Guid)) return element.GetGuid();
        if (fieldType == typeof(TimeSpan)) return element.Deserialize<TimeSpan>();
        if (fieldType == typeof(DateOnly)) return element.Deserialize<DateOnly>();
        if (fieldType == typeof(TimeOnly)) return element.Deserialize<TimeOnly>();

        return element.Deserialize(fieldType);
    }

    /// <summary>
    /// A restricted type converter that only permits types from the System namespace
    /// and common value types, preventing arbitrary type instantiation during deserialization.
    /// </summary>
    internal sealed class SafeTypeConverter : JsonConverter<Type>
    {
        private static readonly HashSet<Type> AllowedTypes =
        [
            typeof(bool),
            typeof(byte),
            typeof(byte[]),
            typeof(char),
            typeof(DateTime),
            typeof(DateOnly),
            typeof(DateTimeOffset),
            typeof(decimal),
            typeof(double),
            typeof(float),
            typeof(Guid),
            typeof(short),
            typeof(int),
            typeof(long),
            typeof(object),
            typeof(sbyte),
            typeof(string),
            typeof(TimeOnly),
            typeof(TimeSpan),
            typeof(ushort),
            typeof(uint),
            typeof(ulong)
        ];

        public static string TypeToString(Type type) => type.AssemblyQualifiedName!;

        public static Type StringToType(string typeName)
        {
            var type = Type.GetType(typeName)
                       ?? throw new JsonException($"Cannot resolve type '{typeName}'.");

            if (!AllowedTypes.Contains(type))
                throw new JsonException($"Type '{typeName}' is not allowed for deserialization.");

            return type;
        }

        public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var typeName = reader.GetString();
            return typeName is null ? null : StringToType(typeName);
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(TypeToString(value));
        }
    }
}
