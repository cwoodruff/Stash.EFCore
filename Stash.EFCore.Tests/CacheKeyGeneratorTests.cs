using Xunit;
using FluentAssertions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;

namespace Stash.EFCore.Tests;

public class CacheKeyGeneratorTests
{
    [Fact]
    public void GenerateKey_SameCommandText_ProducesSameKey()
    {
        // TODO: Implement test with mock DbCommand
    }

    [Fact]
    public void GenerateKey_DifferentParameters_ProducesDifferentKeys()
    {
        // TODO: Implement test with mock DbCommand
    }

    [Fact]
    public void GenerateKey_IncludesConfiguredPrefix()
    {
        // TODO: Verify key starts with configured prefix
    }
}
