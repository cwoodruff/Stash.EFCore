using System.Data;
using System.Data.Common;
using Xunit;
using FluentAssertions;
using Stash.EFCore.Caching;
using Stash.EFCore.Data;

namespace Stash.EFCore.Tests;

public class CacheableResultSetCaptureTests
{
    [Fact]
    public async Task CaptureAsync_AllCommonTypes_CapturesCorrectly()
    {
        var (table, expectedRow) = CreateAllTypesTable();
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Rows.Should().HaveCount(1);
        result.Columns.Should().HaveCount(table.Columns.Count);

        var row = result.Rows[0];
        for (var i = 0; i < expectedRow.Length; i++)
        {
            row[i].Should().BeEquivalentTo(expectedRow[i],
                because: $"column '{table.Columns[i].ColumnName}' (type {table.Columns[i].DataType.Name}) at ordinal {i}");
        }
    }

    [Fact]
    public async Task CaptureAsync_ReadsColumnSchema()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int)).AllowDBNull = false;
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add(1, "test");
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Columns.Should().HaveCount(2);

        result.Columns[0].Name.Should().Be("Id");
        result.Columns[0].FieldType.Should().Be(typeof(int));
        result.Columns[0].Ordinal.Should().Be(0);
        result.Columns[0].AllowDBNull.Should().BeFalse();

        result.Columns[1].Name.Should().Be("Name");
        result.Columns[1].FieldType.Should().Be(typeof(string));
        result.Columns[1].Ordinal.Should().Be(1);
        result.Columns[1].AllowDBNull.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureAsync_DBNullValues_StoredAsNull()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Price", typeof(decimal));
        table.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value);
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Rows[0].Should().AllBeEquivalentTo((object?)null);
    }

    [Fact]
    public async Task CaptureAsync_ExceedsMaxRows_ReturnsNull()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        for (var i = 0; i < 10; i++) table.Rows.Add(i);
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 5);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAsync_ExactlyMaxRows_Succeeds()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        for (var i = 0; i < 5; i++) table.Rows.Add(i);
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 5);

        result.Should().NotBeNull();
        result!.Rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task CaptureAsync_EmptyResultSet_ReturnsEmptyRows()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Rows.Should().BeEmpty();
        result.Columns.Should().HaveCount(2);
    }

    [Fact]
    public async Task CaptureAsync_SingleRow_Succeeds()
    {
        var table = new DataTable();
        table.Columns.Add("Value", typeof(string));
        table.Rows.Add("only");
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Rows.Should().ContainSingle();
        result.Rows[0][0].Should().Be("only");
    }

    [Fact]
    public async Task CaptureAsync_SingleColumn_Succeeds()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        table.Rows.Add(2);
        table.Rows.Add(3);
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Columns.Should().ContainSingle();
        result.Rows.Select(r => r[0]).Should().BeEquivalentTo(new object[] { 1, 2, 3 });
    }

    [Fact]
    public async Task CaptureAsync_AllNullRow_Succeeds()
    {
        var table = new DataTable();
        table.Columns.Add("A", typeof(int));
        table.Columns.Add("B", typeof(string));
        table.Columns.Add("C", typeof(decimal));
        table.Columns.Add("D", typeof(DateTime));
        table.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.Rows[0].Should().OnlyContain(v => v == null);
    }

    [Fact]
    public async Task CaptureAsync_ComputesApproximateSize()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add(1, "hello");
        using var reader = table.CreateDataReader();

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.ApproximateSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CaptureAsync_SetsCapturedAtUtc()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        using var reader = table.CreateDataReader();
        var before = DateTimeOffset.UtcNow;

        var result = await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        result.Should().NotBeNull();
        result!.CapturedAtUtc.Should().BeOnOrAfter(before);
        result.CapturedAtUtc.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CaptureAsync_ClosesReader()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        var reader = table.CreateDataReader();

        await CacheableResultSet.CaptureAsync(reader, maxRows: 100);

        reader.IsClosed.Should().BeTrue();
    }

    private static (DataTable table, object?[] expectedRow) CreateAllTypesTable()
    {
        var table = new DataTable();
        table.Columns.Add("IntCol", typeof(int));
        table.Columns.Add("LongCol", typeof(long));
        table.Columns.Add("StringCol", typeof(string));
        table.Columns.Add("BoolCol", typeof(bool));
        table.Columns.Add("DateTimeCol", typeof(DateTime));
        table.Columns.Add("DateTimeOffsetCol", typeof(DateTimeOffset));
        table.Columns.Add("DecimalCol", typeof(decimal));
        table.Columns.Add("DoubleCol", typeof(double));
        table.Columns.Add("FloatCol", typeof(float));
        table.Columns.Add("GuidCol", typeof(Guid));
        table.Columns.Add("ByteArrayCol", typeof(byte[]));
        table.Columns.Add("ByteCol", typeof(byte));
        table.Columns.Add("ShortCol", typeof(short));
        table.Columns.Add("CharCol", typeof(char));

        var dateTime = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var dateTimeOffset = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(-5));
        var guid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var bytes = new byte[] { 0x01, 0x02, 0xFF };

        object?[] expectedRow =
        [
            42, 9_876_543_210L, "Hello, World!", true,
            dateTime, dateTimeOffset, 123.456m, 3.14159265, 2.71828f,
            guid, bytes, (byte)0xAB, (short)1234, 'Z'
        ];

        table.Rows.Add(expectedRow);
        return (table, expectedRow);
    }
}

public class CachedDataReaderTests
{
    [Fact]
    public void Read_AdvancesThroughRows()
    {
        var resultSet = CreateResultSet(3);
        using var reader = new CachedDataReader(resultSet);

        reader.HasRows.Should().BeTrue();
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(0);
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(2);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_AdvancesThroughRows()
    {
        var resultSet = CreateResultSet(2);
        await using var reader = new CachedDataReader(resultSet);

        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(0);
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        (await reader.ReadAsync()).Should().BeFalse();
    }

    [Fact]
    public void GetValue_ReturnsDBNullForNullValues()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Val", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int), AllowDBNull = true }],
            Rows = [new object?[] { null }]
        };
        using var reader = new CachedDataReader(resultSet);

        reader.Read();
        reader.GetValue(0).Should().Be(DBNull.Value);
        reader.IsDBNull(0).Should().BeTrue();
    }

    [Fact]
    public void GetFieldValue_ThrowsOnNull()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Val", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) }],
            Rows = [new object?[] { null }]
        };
        using var reader = new CachedDataReader(resultSet);
        reader.Read();

        var act = () => reader.GetFieldValue<int>(0);
        act.Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void AllTypedGetters_ReturnCorrectValues()
    {
        var guid = Guid.NewGuid();
        var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var resultSet = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Bool", Ordinal = 0, DataTypeName = "bit", FieldType = typeof(bool) },
                new ColumnDefinition { Name = "Byte", Ordinal = 1, DataTypeName = "tinyint", FieldType = typeof(byte) },
                new ColumnDefinition { Name = "Char", Ordinal = 2, DataTypeName = "char", FieldType = typeof(char) },
                new ColumnDefinition { Name = "Short", Ordinal = 3, DataTypeName = "smallint", FieldType = typeof(short) },
                new ColumnDefinition { Name = "Int", Ordinal = 4, DataTypeName = "int", FieldType = typeof(int) },
                new ColumnDefinition { Name = "Long", Ordinal = 5, DataTypeName = "bigint", FieldType = typeof(long) },
                new ColumnDefinition { Name = "Float", Ordinal = 6, DataTypeName = "real", FieldType = typeof(float) },
                new ColumnDefinition { Name = "Double", Ordinal = 7, DataTypeName = "float", FieldType = typeof(double) },
                new ColumnDefinition { Name = "Decimal", Ordinal = 8, DataTypeName = "decimal", FieldType = typeof(decimal) },
                new ColumnDefinition { Name = "String", Ordinal = 9, DataTypeName = "nvarchar", FieldType = typeof(string) },
                new ColumnDefinition { Name = "DateTime", Ordinal = 10, DataTypeName = "datetime", FieldType = typeof(DateTime) },
                new ColumnDefinition { Name = "Guid", Ordinal = 11, DataTypeName = "uniqueidentifier", FieldType = typeof(Guid) },
            ],
            Rows =
            [
                new object?[] { true, (byte)255, 'X', (short)1000, 42, 9_000_000_000L, 1.5f, 2.5, 99.99m, "hello", dt, guid }
            ]
        };
        using var reader = new CachedDataReader(resultSet);
        reader.Read();

        reader.GetBoolean(0).Should().BeTrue();
        reader.GetByte(1).Should().Be(255);
        reader.GetChar(2).Should().Be('X');
        reader.GetInt16(3).Should().Be(1000);
        reader.GetInt32(4).Should().Be(42);
        reader.GetInt64(5).Should().Be(9_000_000_000L);
        reader.GetFloat(6).Should().Be(1.5f);
        reader.GetDouble(7).Should().Be(2.5);
        reader.GetDecimal(8).Should().Be(99.99m);
        reader.GetString(9).Should().Be("hello");
        reader.GetDateTime(10).Should().Be(dt);
        reader.GetGuid(11).Should().Be(guid);
    }

    [Fact]
    public void GetOrdinal_CaseInsensitive()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "ProductId", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) }],
            Rows = [new object?[] { 1 }]
        };
        using var reader = new CachedDataReader(resultSet);

        reader.GetOrdinal("productid").Should().Be(0);
        reader.GetOrdinal("PRODUCTID").Should().Be(0);
        reader.GetOrdinal("ProductId").Should().Be(0);
    }

    [Fact]
    public void GetOrdinal_UnknownColumn_Throws()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) }],
            Rows = []
        };
        using var reader = new CachedDataReader(resultSet);

        var act = () => reader.GetOrdinal("Missing");
        act.Should().Throw<IndexOutOfRangeException>();
    }

    [Fact]
    public void Indexer_ByName_ReturnsValue()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Val", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) }],
            Rows = [new object?[] { 42 }]
        };
        using var reader = new CachedDataReader(resultSet);
        reader.Read();

        ((int)reader["Val"]).Should().Be(42);
    }

    [Fact]
    public void GetBytes_ReturnsCorrectData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Data", Ordinal = 0, DataTypeName = "blob", FieldType = typeof(byte[]) }],
            Rows = [new object?[] { data }]
        };
        using var reader = new CachedDataReader(resultSet);
        reader.Read();

        // Get total length
        reader.GetBytes(0, 0, null, 0, 0).Should().Be(5);

        // Read partial
        var buffer = new byte[3];
        reader.GetBytes(0, 1, buffer, 0, 3).Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 2, 3, 4 });
    }

    [Fact]
    public void GetChars_ReturnsCorrectData()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Text", Ordinal = 0, DataTypeName = "text", FieldType = typeof(string) }],
            Rows = [new object?[] { "abcde" }]
        };
        using var reader = new CachedDataReader(resultSet);
        reader.Read();

        reader.GetChars(0, 0, null, 0, 0).Should().Be(5);

        var buffer = new char[3];
        reader.GetChars(0, 1, buffer, 0, 3).Should().Be(3);
        buffer.Should().BeEquivalentTo(new[] { 'b', 'c', 'd' });
    }

    [Fact]
    public void GetValues_FillsArray()
    {
        var resultSet = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "A", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) },
                new ColumnDefinition { Name = "B", Ordinal = 1, DataTypeName = "text", FieldType = typeof(string) }
            ],
            Rows = [new object?[] { 1, "x" }]
        };
        using var reader = new CachedDataReader(resultSet);
        reader.Read();

        var values = new object[2];
        reader.GetValues(values).Should().Be(2);
        values[0].Should().Be(1);
        values[1].Should().Be("x");
    }

    [Fact]
    public void EmptyResultSet_HasRowsFalse_ReadReturnsFalse()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) }],
            Rows = []
        };
        using var reader = new CachedDataReader(resultSet);

        reader.HasRows.Should().BeFalse();
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void NextResult_AlwaysReturnsFalse()
    {
        var resultSet = CreateResultSet(1);
        using var reader = new CachedDataReader(resultSet);

        reader.NextResult().Should().BeFalse();
    }

    [Fact]
    public void RecordsAffected_ReturnsValueFromResultSet()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [],
            Rows = [],
            RecordsAffected = 5
        };
        using var reader = new CachedDataReader(resultSet);

        reader.RecordsAffected.Should().Be(5);
    }

    [Fact]
    public void Close_SetsIsClosed()
    {
        var resultSet = CreateResultSet(1);
        var reader = new CachedDataReader(resultSet);

        reader.IsClosed.Should().BeFalse();
        reader.Close();
        reader.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Dispose_SetsIsClosed()
    {
        var resultSet = CreateResultSet(1);
        var reader = new CachedDataReader(resultSet);

        reader.Dispose();
        reader.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void ConcurrentReaders_IndependentCursors()
    {
        var resultSet = CreateResultSet(5);

        var reader1 = new CachedDataReader(resultSet);
        var reader2 = new CachedDataReader(resultSet);

        // Advance reader1 to row 2
        reader1.Read();
        reader1.Read();
        reader1.Read();
        reader1.GetInt32(0).Should().Be(2);

        // reader2 should still be at the beginning
        reader2.Read();
        reader2.GetInt32(0).Should().Be(0);

        reader1.Dispose();
        reader2.Dispose();
    }

    [Fact]
    public async Task ConcurrentReaders_ParallelIteration()
    {
        var resultSet = CreateResultSet(100);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            using var reader = new CachedDataReader(resultSet);
            var values = new List<int>();
            while (reader.Read())
                values.Add(reader.GetInt32(0));
            return values;
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        var expected = Enumerable.Range(0, 100).ToList();
        foreach (var result in results)
            result.Should().BeEquivalentTo(expected, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void GetFieldType_ReturnsCorrectType()
    {
        var resultSet = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) },
                new ColumnDefinition { Name = "Name", Ordinal = 1, DataTypeName = "text", FieldType = typeof(string) }
            ],
            Rows = []
        };
        using var reader = new CachedDataReader(resultSet);

        reader.GetFieldType(0).Should().Be(typeof(int));
        reader.GetFieldType(1).Should().Be(typeof(string));
    }

    [Fact]
    public void GetDataTypeName_ReturnsCorrectName()
    {
        var resultSet = new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "integer", FieldType = typeof(int) }],
            Rows = []
        };
        using var reader = new CachedDataReader(resultSet);

        reader.GetDataTypeName(0).Should().Be("integer");
    }

    private static CacheableResultSet CreateResultSet(int rowCount)
    {
        var rows = new object?[rowCount][];
        for (var i = 0; i < rowCount; i++)
            rows[i] = new object?[] { i };

        return new CacheableResultSet
        {
            Columns = [new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) }],
            Rows = rows
        };
    }
}

public class CacheableResultSetSerializerTests
{
    [Fact]
    public void RoundTrip_AllCommonTypes_PreservesValues()
    {
        var guid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var dto = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(-5));
        var bytes = new byte[] { 0x01, 0x02, 0xFF };

        var original = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Int", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) },
                new ColumnDefinition { Name = "Long", Ordinal = 1, DataTypeName = "bigint", FieldType = typeof(long) },
                new ColumnDefinition { Name = "String", Ordinal = 2, DataTypeName = "text", FieldType = typeof(string) },
                new ColumnDefinition { Name = "Bool", Ordinal = 3, DataTypeName = "bit", FieldType = typeof(bool) },
                new ColumnDefinition { Name = "DateTime", Ordinal = 4, DataTypeName = "datetime", FieldType = typeof(DateTime) },
                new ColumnDefinition { Name = "DateTimeOffset", Ordinal = 5, DataTypeName = "datetimeoffset", FieldType = typeof(DateTimeOffset) },
                new ColumnDefinition { Name = "Decimal", Ordinal = 6, DataTypeName = "decimal", FieldType = typeof(decimal) },
                new ColumnDefinition { Name = "Double", Ordinal = 7, DataTypeName = "float", FieldType = typeof(double) },
                new ColumnDefinition { Name = "Float", Ordinal = 8, DataTypeName = "real", FieldType = typeof(float) },
                new ColumnDefinition { Name = "Guid", Ordinal = 9, DataTypeName = "uniqueidentifier", FieldType = typeof(Guid) },
                new ColumnDefinition { Name = "Bytes", Ordinal = 10, DataTypeName = "blob", FieldType = typeof(byte[]) },
                new ColumnDefinition { Name = "Short", Ordinal = 11, DataTypeName = "smallint", FieldType = typeof(short) },
                new ColumnDefinition { Name = "Byte", Ordinal = 12, DataTypeName = "tinyint", FieldType = typeof(byte) },
                new ColumnDefinition { Name = "Char", Ordinal = 13, DataTypeName = "char", FieldType = typeof(char) },
            ],
            Rows =
            [
                new object?[] { 42, 9_876_543_210L, "Hello!", true, dt, dto, 123.456m, 3.14, 2.71f, guid, bytes, (short)1000, (byte)0xAB, 'Z' }
            ],
            ResultSetCount = 1,
            RecordsAffected = -1,
            ApproximateSizeBytes = 512,
            CapturedAtUtc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero)
        };

        var serialized = CacheableResultSetSerializer.Serialize(original);
        var deserialized = CacheableResultSetSerializer.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.Columns.Should().HaveCount(14);
        deserialized.Rows.Should().HaveCount(1);
        deserialized.ResultSetCount.Should().Be(1);
        deserialized.RecordsAffected.Should().Be(-1);
        deserialized.ApproximateSizeBytes.Should().Be(512);
        deserialized.CapturedAtUtc.Should().Be(original.CapturedAtUtc);

        var row = deserialized.Rows[0];
        row[0].Should().Be(42);
        row[1].Should().Be(9_876_543_210L);
        row[2].Should().Be("Hello!");
        row[3].Should().Be(true);
        row[4].Should().Be(dt);
        row[5].Should().Be(dto);
        row[6].Should().Be(123.456m);
        row[7].Should().Be(3.14);
        ((float)row[8]!).Should().BeApproximately(2.71f, 0.001f);
        row[9].Should().Be(guid);
        row[10].Should().BeEquivalentTo(bytes);
        row[11].Should().Be((short)1000);
        row[12].Should().Be((byte)0xAB);
        row[13].Should().Be('Z');
    }

    [Fact]
    public void RoundTrip_NullValues_Preserved()
    {
        var original = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int), AllowDBNull = true },
                new ColumnDefinition { Name = "Name", Ordinal = 1, DataTypeName = "text", FieldType = typeof(string), AllowDBNull = true },
            ],
            Rows =
            [
                new object?[] { null, null }
            ]
        };

        var serialized = CacheableResultSetSerializer.Serialize(original);
        var deserialized = CacheableResultSetSerializer.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.Rows[0].Should().AllBeEquivalentTo((object?)null);
    }

    [Fact]
    public void RoundTrip_EmptyResultSet()
    {
        var original = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) },
            ],
            Rows = []
        };

        var serialized = CacheableResultSetSerializer.Serialize(original);
        var deserialized = CacheableResultSetSerializer.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.Rows.Should().BeEmpty();
        deserialized.Columns.Should().ContainSingle();
    }

    [Fact]
    public void RoundTrip_MultipleRows()
    {
        var original = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition { Name = "Id", Ordinal = 0, DataTypeName = "int", FieldType = typeof(int) },
                new ColumnDefinition { Name = "Name", Ordinal = 1, DataTypeName = "text", FieldType = typeof(string) },
            ],
            Rows =
            [
                new object?[] { 1, "Alice" },
                new object?[] { 2, "Bob" },
                new object?[] { 3, null }
            ]
        };

        var serialized = CacheableResultSetSerializer.Serialize(original);
        var deserialized = CacheableResultSetSerializer.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        deserialized!.Rows.Should().HaveCount(3);
        deserialized.Rows[0][0].Should().Be(1);
        deserialized.Rows[0][1].Should().Be("Alice");
        deserialized.Rows[2][0].Should().Be(3);
        deserialized.Rows[2][1].Should().BeNull();
    }

    [Fact]
    public void RoundTrip_ColumnMetadata_Preserved()
    {
        var original = new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition
                {
                    Name = "Price",
                    Ordinal = 0,
                    DataTypeName = "decimal(18,2)",
                    FieldType = typeof(decimal),
                    AllowDBNull = false
                }
            ],
            Rows = [new object?[] { 19.99m }]
        };

        var serialized = CacheableResultSetSerializer.Serialize(original);
        var deserialized = CacheableResultSetSerializer.Deserialize(serialized);

        deserialized.Should().NotBeNull();
        var col = deserialized!.Columns[0];
        col.Name.Should().Be("Price");
        col.Ordinal.Should().Be(0);
        col.DataTypeName.Should().Be("decimal(18,2)");
        col.FieldType.Should().Be(typeof(decimal));
        col.AllowDBNull.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_CorruptedData_ReturnsNull()
    {
        var garbage = "not valid json"u8.ToArray();
        CacheableResultSetSerializer.Deserialize(garbage).Should().BeNull();
    }

    [Fact]
    public void Deserialize_EmptyArray_ReturnsNull()
    {
        CacheableResultSetSerializer.Deserialize([]).Should().BeNull();
    }

    [Fact]
    public void Deserialize_InvalidTypeInColumn_ReturnsNull()
    {
        // JSON with a disallowed type
        var json = """
        {
            "Columns": [{"Name":"X","Ordinal":0,"DataTypeName":"int","FieldType":"System.Diagnostics.Process, System.Diagnostics.Process","AllowDBNull":false}],
            "Rows": []
        }
        """u8.ToArray();

        CacheableResultSetSerializer.Deserialize(json).Should().BeNull();
    }

    [Fact]
    public async Task FullRoundTrip_CaptureSerializeDeserializeReplay()
    {
        // 1. Create a source DataTable with diverse types
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Active", typeof(bool));
        table.Columns.Add("Score", typeof(decimal));
        table.Columns.Add("Data", typeof(byte[]));

        table.Rows.Add(1, "Alice", true, 95.5m, new byte[] { 1, 2, 3 });
        table.Rows.Add(2, "Bob", false, 88.0m, DBNull.Value);
        table.Rows.Add(3, DBNull.Value, true, DBNull.Value, new byte[] { 4, 5 });

        // 2. Capture from DbDataReader
        using var sourceReader = table.CreateDataReader();
        var captured = await CacheableResultSet.CaptureAsync(sourceReader, maxRows: 100);
        captured.Should().NotBeNull();

        // 3. Serialize → Deserialize
        var serialized = CacheableResultSetSerializer.Serialize(captured!);
        var deserialized = CacheableResultSetSerializer.Deserialize(serialized);
        deserialized.Should().NotBeNull();

        // 4. Replay through CachedDataReader
        using var cachedReader = new CachedDataReader(deserialized!);
        var replayedRows = new List<object?[]>();
        while (cachedReader.Read())
        {
            var values = new object?[cachedReader.FieldCount];
            for (var i = 0; i < cachedReader.FieldCount; i++)
                values[i] = cachedReader.IsDBNull(i) ? null : cachedReader.GetValue(i);
            replayedRows.Add(values);
        }

        // 5. Verify all values match the original
        replayedRows.Should().HaveCount(3);

        replayedRows[0][0].Should().Be(1);
        replayedRows[0][1].Should().Be("Alice");
        replayedRows[0][2].Should().Be(true);
        replayedRows[0][3].Should().Be(95.5m);
        ((byte[])replayedRows[0][4]!).Should().BeEquivalentTo(new byte[] { 1, 2, 3 });

        replayedRows[1][0].Should().Be(2);
        replayedRows[1][1].Should().Be("Bob");
        replayedRows[1][2].Should().Be(false);
        replayedRows[1][3].Should().Be(88.0m);
        replayedRows[1][4].Should().BeNull();

        replayedRows[2][0].Should().Be(3);
        replayedRows[2][1].Should().BeNull();
        replayedRows[2][2].Should().Be(true);
        replayedRows[2][3].Should().BeNull();
        ((byte[])replayedRows[2][4]!).Should().BeEquivalentTo(new byte[] { 4, 5 });
    }
}
