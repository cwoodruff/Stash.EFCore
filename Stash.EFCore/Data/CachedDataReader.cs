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

    /// <summary>
    /// Initializes a new <see cref="CachedDataReader"/> that replays data from the specified cached result set.
    /// </summary>
    /// <param name="resultSet">The cached result set to replay. Must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resultSet"/> is <c>null</c>.</exception>
    public CachedDataReader(CacheableResultSet resultSet)
    {
        ArgumentNullException.ThrowIfNull(resultSet);
        _resultSet = resultSet;
    }

    /// <inheritdoc />
    public override int FieldCount => _resultSet.Columns.Length;

    /// <inheritdoc />
    public override bool HasRows => _resultSet.Rows.Length > 0;

    /// <inheritdoc />
    public override bool IsClosed => _isClosed;

    /// <inheritdoc />
    public override int RecordsAffected => _resultSet.RecordsAffected;

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override bool Read()
    {
        if (_currentRowIndex + 1 < _resultSet.Rows.Length)
        {
            _currentRowIndex++;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read());
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public override string GetName(int ordinal) => _resultSet.Columns[ordinal].Name;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal) => _resultSet.Columns[ordinal].FieldType;

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => _resultSet.Columns[ordinal].DataTypeName;

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        return _resultSet.Rows[_currentRowIndex][ordinal] ?? DBNull.Value;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        var row = _resultSet.Rows[_currentRowIndex];
        var count = Math.Min(values.Length, row.Length);
        for (var i = 0; i < count; i++)
            values[i] = row[i] ?? DBNull.Value;
        return count;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal) => _resultSet.Rows[_currentRowIndex][ordinal] is null;

    /// <inheritdoc />
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsDBNull(ordinal));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetFieldValue<T>(ordinal));
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);

    /// <inheritdoc />
    public override char GetChar(int ordinal) => GetFieldValue<char>(ordinal);

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);

    /// <inheritdoc />
    public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var data = GetFieldValue<byte[]>(ordinal);
        if (buffer is null)
            return data.Length;
        var count = Math.Min(length, (int)(data.Length - dataOffset));
        Array.Copy(data, dataOffset, buffer, bufferOffset, count);
        return count;
    }

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var str = GetString(ordinal);
        if (buffer is null)
            return str.Length;
        var count = Math.Min(length, (int)(str.Length - dataOffset));
        str.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this);

    /// <inheritdoc />
    public override void Close() => _isClosed = true;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _isClosed = true;
        base.Dispose(disposing);
    }
}
