using System.Data;
using Xunit;
using FluentAssertions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Tests.Fakes;

namespace Stash.EFCore.Tests;

public class CacheKeyGeneratorTests
{
    private readonly DefaultCacheKeyGenerator _generator = new(new StashOptions { KeyPrefix = "stash:" });

    [Fact]
    public void GenerateKey_SameCommandText_ProducesSameKey()
    {
        var cmd1 = new FakeDbCommand("SELECT * FROM Products");
        var cmd2 = new FakeDbCommand("SELECT * FROM Products");

        var key1 = _generator.GenerateKey(cmd1);
        var key2 = _generator.GenerateKey(cmd2);

        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateKey_DifferentCommandText_ProducesDifferentKeys()
    {
        var cmd1 = new FakeDbCommand("SELECT * FROM Products");
        var cmd2 = new FakeDbCommand("SELECT * FROM Orders");

        _generator.GenerateKey(cmd1).Should().NotBe(_generator.GenerateKey(cmd2));
    }

    [Fact]
    public void GenerateKey_DifferentParameters_ProducesDifferentKeys()
    {
        var cmd1 = new FakeDbCommand("SELECT * FROM Products WHERE Id = @id",
            new FakeDbParameter("@id", 1, DbType.Int32));
        var cmd2 = new FakeDbCommand("SELECT * FROM Products WHERE Id = @id",
            new FakeDbParameter("@id", 2, DbType.Int32));

        _generator.GenerateKey(cmd1).Should().NotBe(_generator.GenerateKey(cmd2));
    }

    [Fact]
    public void GenerateKey_SameParameters_ProducesSameKey()
    {
        var cmd1 = new FakeDbCommand("SELECT * FROM Products WHERE Id = @id",
            new FakeDbParameter("@id", 42, DbType.Int32));
        var cmd2 = new FakeDbCommand("SELECT * FROM Products WHERE Id = @id",
            new FakeDbParameter("@id", 42, DbType.Int32));

        _generator.GenerateKey(cmd1).Should().Be(_generator.GenerateKey(cmd2));
    }

    [Fact]
    public void GenerateKey_IncludesConfiguredPrefix()
    {
        var generator = new DefaultCacheKeyGenerator(new StashOptions { KeyPrefix = "myapp:" });
        var cmd = new FakeDbCommand("SELECT 1");

        _generator.GenerateKey(cmd).Should().StartWith("stash:");
        generator.GenerateKey(cmd).Should().StartWith("myapp:");
    }

    [Fact]
    public void GenerateKey_NullParameterValue_HandledGracefully()
    {
        var cmd = new FakeDbCommand("SELECT * FROM Products WHERE Name = @name",
            new FakeDbParameter("@name", null, DbType.String));

        var key = _generator.GenerateKey(cmd);
        key.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ExtractTableDependencies_ReturnsTableNames()
    {
        var tables = _generator.ExtractTableDependencies("SELECT * FROM Products JOIN Orders ON 1=1");

        tables.Should().Contain("Products");
        tables.Should().Contain("Orders");
    }

    [Fact]
    public void ExtractTableDependencies_EmptyQuery_ReturnsEmpty()
    {
        var tables = _generator.ExtractTableDependencies("SELECT 1");
        tables.Should().BeEmpty();
    }
}
