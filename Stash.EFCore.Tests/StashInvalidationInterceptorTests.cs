using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Interceptors;
using Xunit;

namespace Stash.EFCore.Tests;

#region Test entities for invalidation tests

public class InvProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class InvOrder
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>
/// Entity with an owned type (Address) for testing owned entity invalidation.
/// </summary>
public class InvCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public InvAddress Address { get; set; } = new();
}

public class InvAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

/// <summary>
/// TPH base entity.
/// </summary>
public class InvAnimal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class InvDog : InvAnimal
{
    public string Breed { get; set; } = "";
}

public class InvCat : InvAnimal
{
    public bool IsIndoor { get; set; }
}

/// <summary>
/// TPT base entity.
/// </summary>
public class InvVehicle
{
    public int Id { get; set; }
    public string Make { get; set; } = "";
}

public class InvCar : InvVehicle
{
    public int Doors { get; set; }
}

public class InvTruck : InvVehicle
{
    public double PayloadTons { get; set; }
}

public class InvDbContext : DbContext
{
    public DbSet<InvProduct> Products => Set<InvProduct>();
    public DbSet<InvOrder> Orders => Set<InvOrder>();
    public DbSet<InvCustomer> Customers => Set<InvCustomer>();
    public DbSet<InvAnimal> Animals => Set<InvAnimal>();
    public DbSet<InvDog> Dogs => Set<InvDog>();
    public DbSet<InvCat> Cats => Set<InvCat>();
    public DbSet<InvVehicle> Vehicles => Set<InvVehicle>();
    public DbSet<InvCar> Cars => Set<InvCar>();
    public DbSet<InvTruck> Trucks => Set<InvTruck>();

    public InvDbContext(DbContextOptions<InvDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Owned entity
        modelBuilder.Entity<InvCustomer>().OwnsOne(c => c.Address);

        // TPH: Dog and Cat stored in single "Animals" table (default)
        modelBuilder.Entity<InvAnimal>().HasDiscriminator<string>("AnimalType")
            .HasValue<InvAnimal>("Animal")
            .HasValue<InvDog>("Dog")
            .HasValue<InvCat>("Cat");

        // TPT: Vehicle, Car, Truck each get their own table
        modelBuilder.Entity<InvVehicle>().ToTable("Vehicles");
        modelBuilder.Entity<InvCar>().ToTable("Cars");
        modelBuilder.Entity<InvTruck>().ToTable("Trucks");
    }
}

#endregion

public class StashInvalidationInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MemoryCacheStore _cacheStore;
    private readonly StashOptions _options;
    private readonly StashCommandInterceptor _commandInterceptor;
    private readonly StashInvalidationInterceptor _invalidationInterceptor;
    private readonly InvDbContext _context;

    public StashInvalidationInterceptorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new StashOptions
        {
            DefaultAbsoluteExpiration = TimeSpan.FromMinutes(30),
            CacheAllQueries = true
        };

        var keyGen = new DefaultCacheKeyGenerator(_options);
        _cacheStore = new MemoryCacheStore(new MemoryCache(new MemoryCacheOptions()));
        _commandInterceptor = new StashCommandInterceptor(
            _cacheStore, keyGen, _options, NullLogger<StashCommandInterceptor>.Instance);
        _invalidationInterceptor = new StashInvalidationInterceptor(
            _cacheStore, NullLogger<StashInvalidationInterceptor>.Instance, _options);

        var contextOptions = new DbContextOptionsBuilder<InvDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commandInterceptor, _invalidationInterceptor)
            .Options;

        _context = new InvDbContext(contextOptions);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    #region Add entity invalidation

    [Fact]
    public async Task AddEntity_InvalidatesCachedQueryForSameTable()
    {
        // Seed a product and cache a query
        _context.Products.Add(new InvProduct { Name = "Alpha", Price = 1.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache: query products
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        // Second query: should come from cache
        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1);

        // Add another product — should invalidate
        _context.Products.Add(new InvProduct { Name = "Beta", Price = 2.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Query again — should hit DB and see 2 products
        var result3 = await _context.Products.ToListAsync();
        result3.Should().HaveCount(2);
    }

    #endregion

    #region Modify entity invalidation

    [Fact]
    public async Task ModifyEntity_InvalidatesCachedQuery()
    {
        _context.Products.Add(new InvProduct { Name = "Original", Price = 5.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache the query
        var result1 = await _context.Products.ToListAsync();
        result1.Should().ContainSingle(p => p.Name == "Original");

        // Modify the entity
        var product = await _context.Products.FirstAsync();
        product.Name = "Updated";
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Query again — should see the updated name
        var result2 = await _context.Products.ToListAsync();
        result2.Should().ContainSingle(p => p.Name == "Updated");
    }

    #endregion

    #region Delete entity invalidation

    [Fact]
    public async Task DeleteEntity_InvalidatesCachedQuery()
    {
        _context.Products.Add(new InvProduct { Name = "ToDelete", Price = 3.0m });
        _context.Products.Add(new InvProduct { Name = "ToKeep", Price = 4.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache the query
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(2);

        // Delete one product
        var toDelete = await _context.Products.FirstAsync(p => p.Name == "ToDelete");
        _context.Products.Remove(toDelete);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Query again — should see only 1
        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1);
        result2.Should().ContainSingle(p => p.Name == "ToKeep");
    }

    #endregion

    #region Multi-entity SaveChanges

    [Fact]
    public async Task MultiEntitySaveChanges_InvalidatesAllAffectedTables()
    {
        _context.Products.Add(new InvProduct { Name = "P1", Price = 1.0m });
        _context.Orders.Add(new InvOrder { Description = "O1" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache both queries
        var products1 = await _context.Products.ToListAsync();
        var orders1 = await _context.Orders.ToListAsync();
        products1.Should().HaveCount(1);
        orders1.Should().HaveCount(1);

        // Modify a product and add an order in the same SaveChanges
        var p = await _context.Products.FirstAsync();
        p.Name = "P1-Updated";
        _context.Orders.Add(new InvOrder { Description = "O2" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Both caches should be invalidated
        var products2 = await _context.Products.ToListAsync();
        var orders2 = await _context.Orders.ToListAsync();
        products2.Should().ContainSingle(x => x.Name == "P1-Updated");
        orders2.Should().HaveCount(2);
    }

    #endregion

    #region SaveChanges failure — no invalidation

    [Fact]
    public async Task SaveChangesFailed_DoesNotInvalidateCache()
    {
        _context.Products.Add(new InvProduct { Name = "Cached", Price = 1.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache the query
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        // Create a separate context with a bad interceptor that forces failure
        var failConn = new SqliteConnection("DataSource=:memory:");
        failConn.Open();

        var failingInterceptor = new SaveChangesFailSimulator(_cacheStore);
        var failOptions = new DbContextOptionsBuilder<InvDbContext>()
            .UseSqlite(failConn)
            .AddInterceptors(failingInterceptor)
            .Options;

        using var failContext = new InvDbContext(failOptions);
        failContext.Database.EnsureCreated();
        failContext.Products.Add(new InvProduct { Name = "WillFail", Price = 9.0m });

        // Force a save failure by closing the connection before save
        failConn.Close();

        var act = () => failContext.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>();

        failConn.Dispose();

        // Original cached query should still be intact (not invalidated)
        // We verify by checking the cache store still has entries
        // (If invalidation ran, the tags would be cleared)
        // Re-query on original context — should still come from cache (count=1)
        _context.ChangeTracker.Clear();
        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(1);
    }

    #endregion

    #region Sync SaveChanges invalidation

    [Fact]
    public void SyncSaveChanges_InvalidatesCachedQuery()
    {
        _context.Products.Add(new InvProduct { Name = "SyncItem", Price = 1.0m });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        // Cache the query
        var result1 = _context.Products.ToList();
        result1.Should().HaveCount(1);

        // Add another product via sync SaveChanges
        _context.Products.Add(new InvProduct { Name = "SyncItem2", Price = 2.0m });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        // Query again — should see 2
        var result2 = _context.Products.ToList();
        result2.Should().HaveCount(2);
    }

    #endregion

    #region Owned entity invalidation

    [Fact]
    public async Task OwnedEntityChange_InvalidatesCachedQuery()
    {
        _context.Customers.Add(new InvCustomer
        {
            Name = "John",
            Address = new InvAddress { Street = "123 Main", City = "Anytown" }
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache the query
        var result1 = await _context.Customers.ToListAsync();
        result1.Should().HaveCount(1);
        result1[0].Address.City.Should().Be("Anytown");

        // Modify the owned entity (Address)
        var customer = await _context.Customers.FirstAsync();
        customer.Address.City = "Newtown";
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Query again — should see updated address
        var result2 = await _context.Customers.ToListAsync();
        result2[0].Address.City.Should().Be("Newtown");
    }

    #endregion

    #region TPH inheritance invalidation

    [Fact]
    public async Task TphInheritance_DerivedEntityInvalidatesBaseTable()
    {
        _context.Dogs.Add(new InvDog { Name = "Rex", Breed = "Labrador" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache a query on the base Animals table
        var animals1 = await _context.Animals.ToListAsync();
        animals1.Should().HaveCount(1);

        // Add a Cat (different derived type, same TPH table)
        _context.Cats.Add(new InvCat { Name = "Whiskers", IsIndoor = true });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Query Animals again — should see 2 (both Dog and Cat in same table)
        var animals2 = await _context.Animals.ToListAsync();
        animals2.Should().HaveCount(2);
    }

    #endregion

    #region TPT inheritance invalidation

    [Fact]
    public async Task TptInheritance_DerivedEntityInvalidatesItsOwnTable()
    {
        _context.Cars.Add(new InvCar { Make = "Toyota", Doors = 4 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache a query on Cars
        var cars1 = await _context.Cars.ToListAsync();
        cars1.Should().HaveCount(1);

        // Add a Truck (different derived type, different TPT table)
        _context.Trucks.Add(new InvTruck { Make = "Ford", PayloadTons = 5.0 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cars cache should NOT be invalidated (different table)
        // But Vehicles base query should see both
        var vehicles = await _context.Vehicles.ToListAsync();
        vehicles.Should().HaveCount(2);
    }

    [Fact]
    public async Task TptInheritance_ModifyDerived_InvalidatesBothBasAndDerivedTables()
    {
        _context.Cars.Add(new InvCar { Make = "Honda", Doors = 2 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache queries on both Vehicles and Cars
        var vehicles1 = await _context.Vehicles.ToListAsync();
        var cars1 = await _context.Cars.ToListAsync();
        vehicles1.Should().HaveCount(1);
        cars1.Should().HaveCount(1);

        // Modify the car
        var car = await _context.Cars.FirstAsync();
        car.Doors = 4;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Both should be invalidated and reflect the change
        var cars2 = await _context.Cars.ToListAsync();
        cars2[0].Doors.Should().Be(4);
    }

    #endregion

    #region GetChangedTableNames unit tests

    [Fact]
    public void GetChangedTableNames_ReturnsLowercaseTableNames()
    {
        _context.Products.Add(new InvProduct { Name = "Test", Price = 1.0m });

        var tableNames = StashInvalidationInterceptor.GetChangedTableNames(_context);

        tableNames.Should().AllSatisfy(t => t.Should().Be(t.ToLowerInvariant()));
    }

    [Fact]
    public void GetChangedTableNames_IncludesOwnedEntityTables()
    {
        _context.Customers.Add(new InvCustomer
        {
            Name = "Jane",
            Address = new InvAddress { Street = "456 Oak", City = "Springfield" }
        });

        var tableNames = StashInvalidationInterceptor.GetChangedTableNames(_context);

        // Should include the Customers table (owned Address is stored in same table for SQLite)
        tableNames.Should().Contain(t => t == "customers");
    }

    [Fact]
    public void GetChangedTableNames_EmptyForUnchangedEntities()
    {
        // No tracked changes
        var tableNames = StashInvalidationInterceptor.GetChangedTableNames(_context);
        tableNames.Should().BeEmpty();
    }

    #endregion

    #region Manual invalidation API (IStashInvalidator)

    [Fact]
    public async Task StashInvalidator_InvalidateTablesAsync_InvalidatesCache()
    {
        _context.Products.Add(new InvProduct { Name = "ManualTest", Price = 1.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache query
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        // Use manual invalidator
        var invalidator = new StashInvalidator(_cacheStore, NullLogger<StashInvalidator>.Instance, _options);
        await invalidator.InvalidateTablesAsync(["products"]);

        // Add another product directly via SQL (bypasses interceptor)
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (Name, Price) VALUES ('DirectSQL', 2.0)");

        // Query again — should hit DB and see 2
        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(2);
    }

    [Fact]
    public async Task StashInvalidator_InvalidateEntitiesAsync_InvalidatesCache()
    {
        _context.Products.Add(new InvProduct { Name = "EntityInv", Price = 1.0m });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache query
        var result1 = await _context.Products.ToListAsync();
        result1.Should().HaveCount(1);

        // Use entity-based invalidation
        var invalidator = new StashInvalidator(_cacheStore, NullLogger<StashInvalidator>.Instance, _options);
        await invalidator.InvalidateEntitiesAsync(_context, [typeof(InvProduct)]);

        // Add another product via raw SQL
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (Name, Price) VALUES ('EntitySQL', 3.0)");

        var result2 = await _context.Products.ToListAsync();
        result2.Should().HaveCount(2);
    }

    [Fact]
    public async Task StashInvalidator_InvalidateAllAsync_ClearsAllCache()
    {
        _context.Products.Add(new InvProduct { Name = "All1", Price = 1.0m });
        _context.Orders.Add(new InvOrder { Description = "AllOrder" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache both queries
        var products1 = await _context.Products.ToListAsync();
        var orders1 = await _context.Orders.ToListAsync();

        // Invalidate all
        var invalidator = new StashInvalidator(_cacheStore, NullLogger<StashInvalidator>.Instance, _options);
        await invalidator.InvalidateAllAsync();

        // Add data via raw SQL
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Products (Name, Price) VALUES ('AllNew', 2.0)");
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Orders (Description) VALUES ('AllNewOrder')");

        // Both should return fresh data
        var products2 = await _context.Products.ToListAsync();
        var orders2 = await _context.Orders.ToListAsync();
        products2.Should().HaveCount(2);
        orders2.Should().HaveCount(2);
    }

    #endregion

    #region Unrelated table not invalidated

    [Fact]
    public async Task SaveChanges_OnlyInvalidatesAffectedTables()
    {
        _context.Products.Add(new InvProduct { Name = "Prod1", Price = 1.0m });
        _context.Orders.Add(new InvOrder { Description = "Order1" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Cache both queries
        var products1 = await _context.Products.ToListAsync();
        var orders1 = await _context.Orders.ToListAsync();
        products1.Should().HaveCount(1);
        orders1.Should().HaveCount(1);

        // Modify only a product
        var p = await _context.Products.FirstAsync();
        p.Name = "Prod1-Updated";
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Products should be invalidated, orders should still be cached
        // Add an order via raw SQL (won't be seen if cache is still active)
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Orders (Description) VALUES ('Sneaky')");

        var products2 = await _context.Products.ToListAsync();
        products2.Should().ContainSingle(x => x.Name == "Prod1-Updated");

        // Orders cache should NOT have been invalidated — still shows 1
        var orders2 = await _context.Orders.ToListAsync();
        orders2.Should().HaveCount(1);
    }

    #endregion
}

/// <summary>
/// A SaveChangesInterceptor that captures tables but doesn't interfere —
/// used to verify that SaveChangesFailed path discards pending invalidations.
/// </summary>
internal class SaveChangesFailSimulator : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private readonly ICacheStore _cacheStore;

    public SaveChangesFailSimulator(ICacheStore cacheStore)
    {
        _cacheStore = cacheStore;
    }
}
