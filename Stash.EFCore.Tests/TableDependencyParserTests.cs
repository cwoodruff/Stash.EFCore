using Xunit;
using FluentAssertions;
using Stash.EFCore.Data;

namespace Stash.EFCore.Tests;

public class TableDependencyParserTests
{
    [Fact]
    public void ExtractTableNames_SimpleSelect_ReturnsSingleTable()
    {
        var sql = "SELECT * FROM Products WHERE Price > @p0";
        var tables = TableDependencyParser.ExtractTableNames(sql);
        tables.Should().ContainSingle().Which.Should().Be("Products");
    }

    [Fact]
    public void ExtractTableNames_JoinQuery_ReturnsAllTables()
    {
        var sql = "SELECT p.*, c.Name FROM Products p JOIN Categories c ON p.CategoryId = c.Id";
        var tables = TableDependencyParser.ExtractTableNames(sql);
        tables.Should().BeEquivalentTo("Products", "Categories");
    }

    [Fact]
    public void ExtractTableNames_QuotedTableNames_ReturnsUnquotedNames()
    {
        var sql = """SELECT * FROM "Products" WHERE "Price" > @p0""";
        var tables = TableDependencyParser.ExtractTableNames(sql);
        tables.Should().ContainSingle().Which.Should().Be("Products");
    }
}
