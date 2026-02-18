using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Stash.EFCore.Caching;

namespace Stash.EFCore.Tests;

public class HybridCacheStoreTests
{
    private static (HybridCacheStore Store, IServiceProvider Services) CreateStore()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
#pragma warning disable EXTEXP0018 // HybridCache is experimental
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        var sp = services.BuildServiceProvider();
        var hybridCache = sp.GetRequiredService<HybridCache>();
        var store = new HybridCacheStore(hybridCache);
        return (store, sp);
    }

    private static CacheableResultSet CreateSampleResultSet(int rowCount = 2)
    {
        var rows = new object?[rowCount][];
        for (var i = 0; i < rowCount; i++)
            rows[i] = [i + 1];

        return new CacheableResultSet
        {
            Columns =
            [
                new ColumnDefinition
                {
                    Name = "Id",
                    DataTypeName = "integer",
                    FieldType = typeof(int),
                    Ordinal = 0
                }
            ],
            Rows = rows,
            ApproximateSizeBytes = 128,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
    }

    #region Get / Set round-trip

    [Fact]
    public async Task GetAsync_NonexistentKey_ReturnsNull()
    {
        var (store, _) = CreateStore();
        var result = await store.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsCachedValue()
    {
        var (store, _) = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5), tags: ["Products"]);

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
        cached!.Rows.Should().HaveCount(resultSet.Rows.Length);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_PreservesColumnMetadata()
    {
        var (store, _) = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
        cached!.Columns.Should().HaveCount(1);
        cached.Columns[0].Name.Should().Be("Id");
        cached.Columns[0].FieldType.Should().Be(typeof(int));
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_PreservesRowData()
    {
        var (store, _) = CreateStore();
        var resultSet = CreateSampleResultSet(3);
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
        cached!.Rows.Should().HaveCount(3);
        cached.Rows[0][0].Should().Be(1);
        cached.Rows[1][0].Should().Be(2);
        cached.Rows[2][0].Should().Be(3);
    }

    [Fact]
    public async Task SetAsync_UsesSerializationRoundTrip()
    {
        var (store, _) = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("key1");
        // HybridCacheStore serializes to byte[] so the returned object is NOT the same instance
        cached.Should().NotBeNull();
        cached.Should().NotBeSameAs(resultSet, "HybridCacheStore serializes/deserializes via byte[]");
    }

    #endregion

    #region Tag-based invalidation

    [Fact]
    public async Task InvalidateByTagsAsync_RemovesMatchingEntries()
    {
        var (store, _) = CreateStore();
        await store.SetAsync("key1", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);

        await store.InvalidateByTagsAsync(["Products"]);

        var cached = await store.GetAsync("key1");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_DoesNotAffectUnrelatedEntries()
    {
        var (store, _) = CreateStore();
        await store.SetAsync("products", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);
        await store.SetAsync("orders", CreateSampleResultSet(3), TimeSpan.FromMinutes(5), tags: ["Orders"]);

        await store.InvalidateByTagsAsync(["Products"]);

        (await store.GetAsync("products")).Should().BeNull();
        (await store.GetAsync("orders")).Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_NonexistentTag_NoError()
    {
        var (store, _) = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);

        await store.InvalidateByTagsAsync(["NonExistentTable"]);

        (await store.GetAsync("k1")).Should().NotBeNull();
    }

    #endregion

    #region InvalidateAllAsync

    [Fact]
    public async Task InvalidateAllAsync_MakesAllExistingKeysReturnNull()
    {
        var (store, _) = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);
        await store.SetAsync("k2", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Orders"]);
        await store.SetAsync("k3", CreateSampleResultSet(), TimeSpan.FromMinutes(5));

        await store.InvalidateAllAsync();

        (await store.GetAsync("k1")).Should().BeNull();
        (await store.GetAsync("k2")).Should().BeNull();
        (await store.GetAsync("k3")).Should().BeNull();
    }

    [Fact]
    public async Task InvalidateAllAsync_NewEntriesAfterInvalidation_AreAccessible()
    {
        var (store, _) = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5));

        await store.InvalidateAllAsync();

        var newValue = CreateSampleResultSet(5);
        await store.SetAsync("k1", newValue, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("k1");
        cached.Should().NotBeNull();
        cached!.Rows.Should().HaveCount(5);
    }

    #endregion

    #region Concurrent access

    [Fact]
    public async Task ConcurrentSetAndGet_DoesNotThrow()
    {
        var (store, _) = CreateStore();
        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
        {
            var key = $"key{i % 10}";
            var idx = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.SetAsync(key, CreateSampleResultSet(idx + 1), TimeSpan.FromMinutes(5),
                    tags: ["Products"]);
                await store.GetAsync(key);
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentInvalidateAndSet_DoesNotThrow()
    {
        var (store, _) = CreateStore();
        var tasks = new List<Task>();

        for (var i = 0; i < 30; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.SetAsync($"key{idx}", CreateSampleResultSet(), TimeSpan.FromMinutes(5),
                    tags: ["Products", "Orders"]);
            }));
        }

        tasks.Add(Task.Run(async () =>
        {
            await Task.Delay(10);
            await store.InvalidateByTagsAsync(["Products"]);
        }));

        tasks.Add(Task.Run(async () =>
        {
            await Task.Delay(15);
            await store.InvalidateAllAsync();
        }));

        await Task.WhenAll(tasks);
    }

    #endregion

    #region Sliding / local cache expiration

    [Fact]
    public async Task SetAsync_WithSlidingExpiration_SetsLocalCacheExpiration()
    {
        var (store, _) = CreateStore();
        var resultSet = CreateSampleResultSet();

        // Should not throw; LocalCacheExpiration is set from slidingExpiration
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5),
            slidingExpiration: TimeSpan.FromMinutes(1), tags: ["Products"]);

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
    }

    #endregion
}
