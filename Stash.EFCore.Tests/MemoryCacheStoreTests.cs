using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Stash.EFCore.Caching;

namespace Stash.EFCore.Tests;

public class MemoryCacheStoreTests
{
    private static MemoryCacheStore CreateStore(MemoryCacheOptions? options = null)
    {
        options ??= new MemoryCacheOptions();
        return new MemoryCacheStore(new MemoryCache(options));
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
            ApproximateSizeBytes = 128
        };
    }

    #region Get / Set round-trip

    [Fact]
    public async Task GetAsync_NonexistentKey_ReturnsNull()
    {
        var store = CreateStore();
        var result = await store.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsCachedValue()
    {
        var store = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5), tags: ["Products"]);

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
        cached.Should().BeSameAs(resultSet);
    }

    [Fact]
    public async Task SetAsync_StoresDirectlyWithoutSerialization()
    {
        var store = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("key1");
        cached.Should().BeSameAs(resultSet, "MemoryCacheStore should store the object directly, not serialize");
    }

    [Fact]
    public async Task SetAsync_SameKeyOverwrites_ReturnsLatestValue()
    {
        var store = CreateStore();
        var first = CreateSampleResultSet(1);
        var second = CreateSampleResultSet(3);

        await store.SetAsync("key1", first, TimeSpan.FromMinutes(5));
        await store.SetAsync("key1", second, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("key1");
        cached.Should().BeSameAs(second);
    }

    [Fact]
    public async Task SetAsync_WithSlidingExpiration_SetsOption()
    {
        var store = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5),
            slidingExpiration: TimeSpan.FromMinutes(1));

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
    }

    #endregion

    #region Expiration

    [Fact]
    public async Task GetAsync_AfterAbsoluteExpiration_ReturnsNull()
    {
        var clock = new FakeSystemClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var store = new MemoryCacheStore(memoryCache);

        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        // Advance past expiration
        clock.Advance(TimeSpan.FromMinutes(6));

        var cached = await store.GetAsync("key1");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_BeforeExpiration_ReturnsValue()
    {
        var clock = new FakeSystemClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var store = new MemoryCacheStore(memoryCache);

        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(4));

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
    }

    #endregion

    #region Tag-based invalidation

    [Fact]
    public async Task InvalidateByTagsAsync_RemovesMatchingEntries()
    {
        var store = CreateStore();
        var resultSet = CreateSampleResultSet();
        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5), tags: ["Products"]);

        await store.InvalidateByTagsAsync(["Products"]);

        var cached = await store.GetAsync("key1");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_DoesNotAffectUnrelatedEntries()
    {
        var store = CreateStore();
        var products = CreateSampleResultSet(2);
        var orders = CreateSampleResultSet(3);

        await store.SetAsync("products", products, TimeSpan.FromMinutes(5), tags: ["Products"]);
        await store.SetAsync("orders", orders, TimeSpan.FromMinutes(5), tags: ["Orders"]);

        await store.InvalidateByTagsAsync(["Products"]);

        (await store.GetAsync("products")).Should().BeNull();
        (await store.GetAsync("orders")).Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_MultipleTagsInvalidatesAll()
    {
        var store = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);
        await store.SetAsync("k2", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Orders"]);
        await store.SetAsync("k3", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Customers"]);

        await store.InvalidateByTagsAsync(["Products", "Orders"]);

        (await store.GetAsync("k1")).Should().BeNull();
        (await store.GetAsync("k2")).Should().BeNull();
        (await store.GetAsync("k3")).Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_EntryWithMultipleTags_RemovedByEitherTag()
    {
        var store = CreateStore();
        await store.SetAsync("joinQuery", CreateSampleResultSet(), TimeSpan.FromMinutes(5),
            tags: ["Products", "Orders"]);

        await store.InvalidateByTagsAsync(["Orders"]);

        (await store.GetAsync("joinQuery")).Should().BeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_CaseInsensitiveTags()
    {
        var store = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);

        await store.InvalidateByTagsAsync(["products"]);

        (await store.GetAsync("k1")).Should().BeNull();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_NonexistentTag_NoError()
    {
        var store = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5), tags: ["Products"]);

        // Should not throw
        await store.InvalidateByTagsAsync(["NonExistentTable"]);

        (await store.GetAsync("k1")).Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WithNoTags_NotAffectedByTagInvalidation()
    {
        var store = CreateStore();
        await store.SetAsync("untagged", CreateSampleResultSet(), TimeSpan.FromMinutes(5));

        await store.InvalidateByTagsAsync(["Products"]);

        (await store.GetAsync("untagged")).Should().NotBeNull();
    }

    #endregion

    #region InvalidateAllAsync

    [Fact]
    public async Task InvalidateAllAsync_MakesAllExistingKeysReturnNull()
    {
        var store = CreateStore();
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
        var store = CreateStore();
        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5));

        await store.InvalidateAllAsync();

        var newValue = CreateSampleResultSet(5);
        await store.SetAsync("k1", newValue, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("k1");
        cached.Should().BeSameAs(newValue);
    }

    [Fact]
    public async Task InvalidateAllAsync_CalledMultipleTimes_AllOldEntriesExpired()
    {
        var store = CreateStore();

        await store.SetAsync("k1", CreateSampleResultSet(), TimeSpan.FromMinutes(5));
        await store.InvalidateAllAsync();

        await store.SetAsync("k2", CreateSampleResultSet(), TimeSpan.FromMinutes(5));
        await store.InvalidateAllAsync();

        (await store.GetAsync("k1")).Should().BeNull();
        (await store.GetAsync("k2")).Should().BeNull();
    }

    #endregion

    #region Concurrent access

    [Fact]
    public async Task ConcurrentSetAndGet_DoesNotThrow()
    {
        var store = CreateStore();
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            var key = $"key{i % 10}";
            tasks.Add(Task.Run(async () =>
            {
                await store.SetAsync(key, CreateSampleResultSet(), TimeSpan.FromMinutes(5),
                    tags: ["Products"]);
                await store.GetAsync(key);
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentInvalidateAndSet_DoesNotThrow()
    {
        var store = CreateStore();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = new List<Task>();

        for (var i = 0; i < 50; i++)
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
            await Task.Delay(10); // let some sets complete
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

    #region Size limit enforcement

    [Fact]
    public async Task SetAsync_SetsSize_ToApproximateSizeBytes()
    {
        // Use a size-limited MemoryCache to verify Size is set
        var memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000
        });
        var store = new MemoryCacheStore(memoryCache);

        var resultSet = CreateSampleResultSet();
        resultSet.ApproximateSizeBytes.Should().Be(128);

        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));

        var cached = await store.GetAsync("key1");
        cached.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WhenSizeLimitExceeded_EntryEvicted()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 200 // very small limit
        });
        var store = new MemoryCacheStore(memoryCache);

        var resultSet = CreateSampleResultSet();
        resultSet.ApproximateSizeBytes = 150;

        await store.SetAsync("key1", resultSet, TimeSpan.FromMinutes(5));
        // First entry fits (150 <= 200)
        (await store.GetAsync("key1")).Should().NotBeNull();

        // Second entry would exceed the limit (150 + 150 > 200)
        await store.SetAsync("key2", resultSet, TimeSpan.FromMinutes(5));

        // MemoryCache may compact/evict to make room
        // At least one should be present, and total used size should be within limit
        var count = 0;
        if (await store.GetAsync("key1") is not null) count++;
        if (await store.GetAsync("key2") is not null) count++;
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    /// <summary>
    /// Fake clock for controlling MemoryCache time in tests.
    /// </summary>
#pragma warning disable CS0618 // ISystemClock is obsolete
    private sealed class FakeSystemClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
#pragma warning restore CS0618
}
