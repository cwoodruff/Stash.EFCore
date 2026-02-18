using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Extensions;
using Xunit;

namespace Stash.EFCore.Tests.Integration;

public class ConcurrencyTests : IDisposable
{
    private readonly CacheTestFixture _f;

    public ConcurrencyTests()
    {
        _f = new CacheTestFixture();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task ParallelIdenticalQueries_AllReturnCorrectResults()
    {
        // Run 100 parallel identical cached queries
        var tasks = Enumerable.Range(0, 100).Select(async _ =>
        {
            using var ctx = _f.CreateContext();
            return await ctx.Products.Cached().ToListAsync();
        });

        var results = await Task.WhenAll(tasks);

        // All should return 50 products
        results.Should().AllSatisfy(r => r.Should().HaveCount(50));

        // The cache + DB should have been hit a small number of times (ideally 1,
        // but race conditions may cause a few more — certainly not 100)
        _f.SqlCounter.ExecutionCount.Should().BeLessThan(10,
            "most queries should be served from cache after the first fills it");
    }

    [Fact]
    public async Task ConcurrentSaveChanges_DifferentTables_CorrectInvalidation()
    {
        // Pre-cache product and category queries
        using (var ctx = _f.CreateContext())
        {
            await ctx.Products.Cached().ToListAsync();
            await ctx.Categories.Cached().ToListAsync();
        }

        // Concurrently modify products and orders (not categories)
        var task1 = Task.Run(async () =>
        {
            using var ctx = _f.CreateContext();
            ctx.Products.Add(new Product
            {
                Name = "ConcurrentProd", Price = 1.0m, IsActive = true, CategoryId = 1
            });
            await ctx.SaveChangesAsync();
        });

        var task2 = Task.Run(async () =>
        {
            using var ctx = _f.CreateContext();
            ctx.Orders.Add(new Order
            {
                CustomerName = "ConcurrentCustomer", OrderDate = DateTime.Now
            });
            await ctx.SaveChangesAsync();
        });

        await Task.WhenAll(task1, task2);

        // Products cache should be invalidated, categories should still be cached
        _f.SqlCounter.Reset();
        using var verifyCtx = _f.CreateContext();

        var products = await verifyCtx.Products.Cached().ToListAsync();
        var prodHits = _f.SqlCounter.ExecutionCount;
        prodHits.Should().Be(1, "products cache was invalidated");
        products.Should().HaveCount(51);

        _f.SqlCounter.Reset();
        var categories = await verifyCtx.Categories.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "categories cache should be intact");
        categories.Should().HaveCount(5);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_NoDeadlock()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Mix of concurrent reads and writes
        var readTasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            using var ctx = _f.CreateContext();
            return await ctx.Products.Cached().ToListAsync(cts.Token);
        });

        var writeTasks = Enumerable.Range(0, 10).Select(async i =>
        {
            using var ctx = _f.CreateContext();
            ctx.AppSettings.Add(new AppSetting
            {
                Key = $"Concurrent-{i}", Value = $"Value-{i}"
            });
            await ctx.SaveChangesAsync(cts.Token);
        });

        var allTasks = readTasks.Cast<Task>().Concat(writeTasks);

        // Should complete without deadlock within the timeout
        await Task.WhenAll(allTasks);

        // Verify data integrity
        using var verifyCtx = _f.CreateContext();
        var settings = await verifyCtx.AppSettings.ToListAsync();
        settings.Count.Should().BeGreaterThanOrEqualTo(13, "3 seeded + 10 concurrent writes");
    }

    [Fact]
    public async Task ParallelDifferentQueries_EachCachedIndependently()
    {
        var tasks = Enumerable.Range(1, 5).Select(async catId =>
        {
            using var ctx = _f.CreateContext();
            return await ctx.Products
                .Where(p => p.CategoryId == catId)
                .Cached()
                .ToListAsync();
        });

        var results = await Task.WhenAll(tasks);

        // Each category should have 10 products
        results.Should().AllSatisfy(r => r.Should().HaveCount(10));

        // Re-query all — should all be cached
        _f.SqlCounter.Reset();
        var cacheTasks = Enumerable.Range(1, 5).Select(async catId =>
        {
            using var ctx = _f.CreateContext();
            return await ctx.Products
                .Where(p => p.CategoryId == catId)
                .Cached()
                .ToListAsync();
        });

        var cachedResults = await Task.WhenAll(cacheTasks);
        cachedResults.Should().AllSatisfy(r => r.Should().HaveCount(10));
        _f.SqlCounter.ExecutionCount.Should().Be(0, "all should be served from cache");
    }
}
