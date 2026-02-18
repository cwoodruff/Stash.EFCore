using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Extensions;
using Xunit;

namespace Stash.EFCore.Tests.Integration;

public class InvalidationTests : IDisposable
{
    private readonly CacheTestFixture _f;

    public InvalidationTests()
    {
        _f = new CacheTestFixture();
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task AddProduct_InvalidatesCachedProductList()
    {
        using var ctx = _f.CreateContext();

        // Cache the product list
        var result1 = await ctx.Products.Cached().ToListAsync();
        result1.Should().HaveCount(50);

        // Add a new product — triggers SaveChanges invalidation
        ctx.Products.Add(new Product
        {
            Name = "NewProduct", Price = 99.99m, IsActive = true, CategoryId = 1
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Query again — should hit DB and see 51 products
        _f.SqlCounter.Reset();
        var result2 = await ctx.Products.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1, "cache should have been invalidated");
        result2.Should().HaveCount(51);
    }

    [Fact]
    public async Task UpdateProduct_InvalidatesCachedQuery()
    {
        using var ctx = _f.CreateContext();

        // Cache the query
        var result1 = await ctx.Products.Where(p => p.Id == 1).Cached().ToListAsync();
        result1.Should().ContainSingle();
        var originalName = result1[0].Name;

        // Update the product
        var product = await ctx.Products.FirstAsync(p => p.Id == 1);
        product.Name = "UpdatedName";
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Query again — should see updated name
        _f.SqlCounter.Reset();
        var result2 = await ctx.Products.Where(p => p.Id == 1).Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1, "cache should have been invalidated by update");
        result2[0].Name.Should().Be("UpdatedName");
    }

    [Fact]
    public async Task DeleteProduct_InvalidatesCachedQuery()
    {
        using var ctx = _f.CreateContext();

        // Cache the count
        var count1 = await ctx.Products.Cached().CountAsync();
        count1.Should().Be(50);

        // Delete a product
        var product = await ctx.Products.FirstAsync(p => p.Id == 1);
        ctx.Products.Remove(product);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Count again — should reflect deletion
        _f.SqlCounter.Reset();
        var count2 = await ctx.Products.Cached().CountAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1, "cache should have been invalidated by delete");
        count2.Should().Be(49);
    }

    [Fact]
    public async Task ProductChange_DoesNotInvalidateCategoriesCache()
    {
        using var ctx = _f.CreateContext();

        // Cache both product and category queries
        var products1 = await ctx.Products.Cached().ToListAsync();
        var categories1 = await ctx.Categories.Cached().ToListAsync();
        products1.Should().HaveCount(50);
        categories1.Should().HaveCount(5);

        // Add a product (modifies Products table only)
        ctx.Products.Add(new Product
        {
            Name = "Extra", Price = 1.0m, IsActive = true, CategoryId = 1
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Products cache invalidated, categories cache still valid
        _f.SqlCounter.Reset();
        var products2 = await ctx.Products.Cached().ToListAsync();
        var dbHitsAfterProducts = _f.SqlCounter.ExecutionCount;
        products2.Should().HaveCount(51);
        dbHitsAfterProducts.Should().Be(1, "products cache was invalidated");

        _f.SqlCounter.Reset();
        var categories2 = await ctx.Categories.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "categories cache should NOT be invalidated by product change");
        categories2.Should().HaveCount(5);
    }

    [Fact]
    public async Task SaveChangesFailure_DoesNotInvalidateCache()
    {
        // Use a separate connection so we can close it to force a failure
        using var failFixture = new CacheTestFixture();
        using var ctx = failFixture.CreateContext();

        // Cache a query
        var result1 = await ctx.Products.Cached().ToListAsync();
        result1.Should().HaveCount(50);

        // Force a constraint violation to make SaveChanges fail
        ctx.Products.Add(new Product { Name = "OK", Price = 1.0m, CategoryId = 1 });
        // Add a product referencing a nonexistent category to cause FK violation
        ctx.Products.Add(new Product { Name = "Bad", Price = 1.0m, CategoryId = 99999 });

        var act = () => ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>();

        ctx.ChangeTracker.Clear();

        // The cached query should still be valid (not invalidated by the failed save)
        failFixture.SqlCounter.Reset();
        using var ctx2 = failFixture.CreateContext();
        var result2 = await ctx2.Products.Cached().ToListAsync();
        failFixture.SqlCounter.ExecutionCount.Should().Be(0,
            "failed SaveChanges should not invalidate cache");
        result2.Should().HaveCount(50);
    }

    [Fact]
    public async Task ManualInvalidation_ViaStashInvalidator()
    {
        using var ctx = _f.CreateContext();

        // Cache a query
        var result1 = await ctx.Products.Cached().ToListAsync();
        result1.Should().HaveCount(50);

        _f.SqlCounter.Reset();
        await ctx.Products.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(0, "should be cached");

        // Manually invalidate
        var invalidator = new StashInvalidator(
            _f.CacheStore, NullLogger<StashInvalidator>.Instance, _f.Options);
        await invalidator.InvalidateTablesAsync(["products"]);

        // Next query should hit DB
        _f.SqlCounter.Reset();
        var result2 = await ctx.Products.Cached().ToListAsync();
        _f.SqlCounter.ExecutionCount.Should().Be(1, "manual invalidation should force DB hit");
        result2.Should().HaveCount(50);
    }
}
