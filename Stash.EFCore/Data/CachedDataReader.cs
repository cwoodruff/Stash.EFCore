using System.Collections;
using System.Data.Common;
using Stash.EFCore.Caching;

namespace Stash.EFCore.Data;

/// <summary>
/// A <see cref="DbDataReader"/> that serves rows from a <see cref="CacheableResultSet"/>
/// instead of reading from a live database connection.
/// </summary>
public class CachedDataReader : DbDataReader
{
    private readonly CacheableResultSet _resultSet;
    private int _currentRowIndex = -1;
    private bool _isClosed;

    public CachedDataReader(CacheableResultSet resultSet)
    {
        _resultSet = resultSet;
    }

    public override int FieldCount => _resultSet.Columns.Length;
    public override bool HasRows => _resultSet.Rows.Count > 0;
    public override bool IsClosed => _isClosed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_currentRowIndex + 1 < _resultSet.Rows.Count)
        {
            _currentRowIndex++;
            return true;
        }
        return false;
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => _resultSet.Columns[ordinal].Name;

    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < _resultSet.Columns.Length; i++)
        {
            if (string.Equals(_resultSet.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new IndexOutOfRangeException($"Column '{name}' not found.");
    }

    public override Type GetFieldType(int ordinal) => _resultSet.Columns[ordinal].FieldType;
    public override string GetDataTypeName(int ordinal) => _resultSet.Columns[ordinal].DataTypeName;

    public override object GetValue(int ordinal) => _resultSet.Rows[_currentRowIndex][ordinal] ?? DBNull.Value;

    public override int GetValues(object[] values)
    {
        var row = _resultSet.Rows[_currentRowIndex];
        var count = Math.Min(values.Length, row.Length);
        for (var i = 0; i < count; i++)
            values[i] = row[i] ?? DBNull.Value;
        return count;
    }

    public override bool IsDBNull(int ordinal) => _resultSet.Rows[_currentRowIndex][ordinal] is null;

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (GetValue(ordinal) is not byte[] data) return 0;
        if (buffer is null) return data.Length;
        var count = Math.Min(length, (int)(data.Length - dataOffset));
        Array.Copy(data, dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var str = GetString(ordinal);
        if (buffer is null) return str.Length;
        var count = Math.Min(length, (int)(str.Length - dataOffset));
        str.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    public override void Close()
    {
        _isClosed = true;
    }
}
