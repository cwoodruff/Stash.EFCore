using System.Data;
using System.Data.Common;

namespace Stash.EFCore.Tests.Fakes;

/// <summary>
/// Minimal DbCommand for testing cache key generation and interceptor logic.
/// </summary>
internal sealed class FakeDbCommand : DbCommand
{
    private readonly FakeDbParameterCollection _parameters;

    public FakeDbCommand(string commandText, params FakeDbParameter[] parameters)
    {
        CommandText = commandText;
        _parameters = new FakeDbParameterCollection(parameters);
    }

    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;

    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => null;
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        throw new NotSupportedException();
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new FakeDbParameter("", null);
}

/// <summary>
/// Minimal DbParameter for testing.
/// </summary>
internal sealed class FakeDbParameter : DbParameter
{
    public FakeDbParameter(string name, object? value, DbType dbType = DbType.String)
    {
        ParameterName = name;
        Value = value;
        DbType = dbType;
    }

    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; }
    public override int Size { get; set; }
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }

    public override void ResetDbType() => DbType = DbType.String;
}

/// <summary>
/// Minimal DbParameterCollection for testing.
/// </summary>
internal sealed class FakeDbParameterCollection : DbParameterCollection
{
    private readonly List<FakeDbParameter> _parameters;

    public FakeDbParameterCollection(params FakeDbParameter[] parameters)
    {
        _parameters = [.. parameters];
    }

    public override int Count => _parameters.Count;
    public override object SyncRoot { get; } = new();

    public override int Add(object value)
    {
        _parameters.Add((FakeDbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (FakeDbParameter p in values)
            _parameters.Add(p);
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) =>
        _parameters.Contains((FakeDbParameter)value);

    public override bool Contains(string value) =>
        _parameters.Any(p => p.ParameterName == value);

    public override void CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_parameters).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() =>
        _parameters.GetEnumerator();

    public override int IndexOf(object value) =>
        _parameters.IndexOf((FakeDbParameter)value);

    public override int IndexOf(string parameterName) =>
        _parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void Insert(int index, object value) =>
        _parameters.Insert(index, (FakeDbParameter)value);

    public override void Remove(object value) =>
        _parameters.Remove((FakeDbParameter)value);

    public override void RemoveAt(int index) =>
        _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName) =>
        _parameters.RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) =>
        _parameters[index];

    protected override DbParameter GetParameter(string parameterName) =>
        _parameters.First(p => p.ParameterName == parameterName);

    protected override void SetParameter(int index, DbParameter value) =>
        _parameters[index] = (FakeDbParameter)value;

    protected override void SetParameter(string parameterName, DbParameter value) =>
        _parameters[IndexOf(parameterName)] = (FakeDbParameter)value;
}
