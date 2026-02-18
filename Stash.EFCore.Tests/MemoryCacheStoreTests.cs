using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Stash.EFCore.Caching;

namespace Stash.EFCore.Tests;

public class MemoryCacheStoreTests
{
    private readonly MemoryCacheStore _store;

    public MemoryCacheStoreTests()
    {
        _store = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task GetAsync_NonexistentKey_ReturnsNull()
    {
        var result = await _store.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsCachedValue()
    {
        var resultSet = CreateSampleResultSet("Products");
        await _store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        var cached = await _store.GetAsync("key1");
        cached.Should().NotBeNull();
        cached!.Rows.Should().HaveCount(resultSet.Rows.Count);
    }

    [Fact]
    public async Task InvalidateByTagsAsync_RemovesMatchingEntries()
    {
        var resultSet = CreateSampleResultSet("Products");
        await _store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        await _store.InvalidateByTagsAsync(["Products"]);

        var cached = await _store.GetAsync("key1");
        cached.Should().BeNull();
    }

    private static CacheableResultSet CreateSampleResultSet(params string[] tableDependencies)
    {
        return new CacheableResultSet
        {
            Columns =
            [
                new CacheableResultSet.ColumnInfo
                {
                    Name = "Id",
                    DataTypeName = "integer",
                    FieldType = typeof(int),
                    Ordinal = 0
                }
            ],
            Rows = [new object?[] { 1 }, new object?[] { 2 }],
            TableDependencies = tableDependencies
        };
    }
}
