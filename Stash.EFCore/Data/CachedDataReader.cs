using System.Collections;
using System.Data.Common;
using Stash.EFCore.Caching;

namespace Stash.EFCore.Data;

/// <summary>
/// A read-only <see cref="DbDataReader"/> that replays data from a <see cref="CacheableResultSet"/>.
/// Each instance maintains its own cursor position and does not mutate the underlying result set,
/// allowing multiple readers to concurrently iterate the same cached data.
/// </summary>
public sealed class CachedDataReader : DbDataReader
{
    private readonly CacheableResultSet _resultSet;
    private int _currentRowIndex = -1;
    private bool _isClosed;

    public CachedDataReader(CacheableResultSet resultSet)
    {
        ArgumentNullException.ThrowIfNull(resultSet);
        _resultSet = resultSet;
    }

    public override int FieldCount => _resultSet.Columns.Length;
    public override bool HasRows => _resultSet.Rows.Length > 0;
    public override bool IsClosed => _isClosed;
    public override int RecordsAffected => _resultSet.RecordsAffected;
    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_currentRowIndex + 1 < _resultSet.Rows.Length)
        {
            _currentRowIndex++;
            return true;
        }

        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read());
    }

    public override bool NextResult() => false;

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public override string GetName(int ordinal) => _resultSet.Columns[ordinal].Name;

    public override int GetOrdinal(string name)
    {
        var columns = _resultSet.Columns;
        for (var i = 0; i < columns.Length; i++)
        {
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new IndexOutOfRangeException($"Column '{name}' not found.");
    }

    public override Type GetFieldType(int ordinal) => _resultSet.Columns[ordinal].FieldType;
    public override string GetDataTypeName(int ordinal) => _resultSet.Columns[ordinal].DataTypeName;

    public override object GetValue(int ordinal)
    {
        return _resultSet.Rows[_currentRowIndex][ordinal] ?? DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        var row = _resultSet.Rows[_currentRowIndex];
        var count = Math.Min(values.Length, row.Length);
        for (var i = 0; i < count; i++)
            values[i] = row[i] ?? DBNull.Value;
        return count;
    }

    public override bool IsDBNull(int ordinal) => _resultSet.Rows[_currentRowIndex][ordinal] is null;

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsDBNull(ordinal));
    }

    public override T GetFieldValue<T>(int ordinal)
    {
        var value = _resultSet.Rows[_currentRowIndex][ordinal];
        if (value is null)
            throw new InvalidCastException($"Cannot cast DBNull to {typeof(T).Name}.");

        // Fast path: exact type match
        if (value is T typed)
            return typed;

        // Slow path: numeric and other IConvertible conversions (e.g., Int64 → Int32 from SQLite)
        return (T)Convert.ChangeType(value, typeof(T));
    }

    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetFieldValue<T>(ordinal));
    }

    public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);
    public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);
    public override char GetChar(int ordinal) => GetFieldValue<char>(ordinal);
    public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);
    public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);
    public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);
    public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);
    public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);
    public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);
    public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);
    public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);
    public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var data = GetFieldValue<byte[]>(ordinal);
        if (buffer is null)
            return data.Length;
        var count = Math.Min(length, (int)(data.Length - dataOffset));
        Array.Copy(data, dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var str = GetString(ordinal);
        if (buffer is null)
            return str.Length;
        var count = Math.Min(length, (int)(str.Length - dataOffset));
        str.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    public override void Close() => _isClosed = true;

    protected override void Dispose(bool disposing)
    {
        _isClosed = true;
        base.Dispose(disposing);
    }
}
