using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Caching;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Sample.Data;
using Stash.EFCore.Sample.Models;

var builder = WebApplication.CreateBuilder(args);

// ── JSON: handle circular references from navigation properties ──
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);

// ── Logging: show Stash cache hit/miss messages in the console ──
builder.Logging.AddFilter("Stash.EFCore", LogLevel.Debug);

// ── 1. Register Stash.EFCore ──
builder.Services.AddStash(options =>
{
    options.DefaultAbsoluteExpiration = TimeSpan.FromMinutes(15);
    options.DefaultSlidingExpiration = TimeSpan.FromMinutes(5);
    options.EnableLogging = true;
    options.MaxRowsPerQuery = 5000;

    // Named profiles
    options.Profiles["hot-data"] = new StashProfile
    {
        Name = "hot-data",
        AbsoluteExpiration = TimeSpan.FromMinutes(1),
        SlidingExpiration = TimeSpan.FromSeconds(30)
    };
    options.Profiles["reference-data"] = new StashProfile
    {
        Name = "reference-data",
        AbsoluteExpiration = TimeSpan.FromHours(4)
    };

    // Never cache audit logs
    options.ExcludedTables.Add("AuditLogs");
});

// ── 2. Register DbContext with Stash interceptors ──
builder.Services.AddDbContext<CatalogDbContext>((sp, options) =>
{
    options.UseSqlite("Data Source=catalog.db");
    options.UseStash(sp);
});

var app = builder.Build();

// ── Ensure database is created with seed data ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    db.Database.EnsureCreated();
}

// ─────────────────────────── API Endpoints ───────────────────────────

// GET /products — cached list with default TTL
app.MapGet("/products", async (CatalogDbContext db) =>
    await db.Products
        .Include(p => p.Category)
        .Include(p => p.Supplier)
        .Where(p => p.IsActive)
        .OrderBy(p => p.Name)
        .Cached()
        .ToListAsync());

// GET /products/{id} — cached by ID with explicit TTL
app.MapGet("/products/{id:int}", async (int id, CatalogDbContext db) =>
    await db.Products
        .Include(p => p.Category)
        .Include(p => p.Supplier)
        .Where(p => p.Id == id)
        .Cached(TimeSpan.FromMinutes(10))
        .FirstOrDefaultAsync()
    is { } product
        ? Results.Ok(product)
        : Results.NotFound());

// GET /categories — reference data with long-lived cache profile
app.MapGet("/categories", async (CatalogDbContext db) =>
    await db.Categories
        .OrderBy(c => c.Name)
        .Cached("reference-data")
        .ToListAsync());

// GET /suppliers — hot data with short-lived cache profile
app.MapGet("/suppliers", async (CatalogDbContext db) =>
    await db.Suppliers
        .OrderBy(s => s.Name)
        .Cached("hot-data")
        .ToListAsync());

// GET /products/search?q=...&minPrice=...&maxPrice=... — cached search
app.MapGet("/products/search", async (
    string? q, decimal? minPrice, decimal? maxPrice,
    CatalogDbContext db) =>
{
    var query = db.Products.Include(p => p.Category).AsQueryable();

    if (!string.IsNullOrEmpty(q))
        query = query.Where(p => p.Name.Contains(q));
    if (minPrice.HasValue)
        query = query.Where(p => p.Price >= minPrice);
    if (maxPrice.HasValue)
        query = query.Where(p => p.Price <= maxPrice);

    return await query
        .OrderBy(p => p.Price)
        .Cached(TimeSpan.FromMinutes(5))
        .ToListAsync();
});

// GET /stats — cached aggregation queries
app.MapGet("/stats", async (CatalogDbContext db) => new
{
    TotalProducts = await db.Products.Cached().CountAsync(),
    ActiveProducts = await db.Products.Where(p => p.IsActive).Cached().CountAsync(),
    AveragePrice = await db.Products.Cached().AverageAsync(p => p.Price),
    CategoryCount = await db.Categories.Cached().CountAsync(),
    SupplierCount = await db.Suppliers.Cached().CountAsync()
});

// GET /audit-logs — NOT cached (table is excluded), demonstrates ExcludedTables
app.MapGet("/audit-logs", async (CatalogDbContext db) =>
    await db.AuditLogs
        .OrderByDescending(a => a.Timestamp)
        .Take(50)
        .ToListAsync());

// POST /products — creates product, auto-invalidates products cache via SaveChanges
app.MapPost("/products", async (CreateProductDto dto, CatalogDbContext db) =>
{
    var product = new Product
    {
        Name = dto.Name,
        Price = dto.Price,
        CategoryId = dto.CategoryId,
        SupplierId = dto.SupplierId
    };
    db.Products.Add(product);
    await db.SaveChangesAsync(); // Triggers auto-invalidation
    return Results.Created($"/products/{product.Id}", product);
});

// PUT /products/{id} — updates product, auto-invalidates via SaveChanges
app.MapPut("/products/{id:int}", async (int id, UpdateProductDto dto, CatalogDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound();

    product.Name = dto.Name;
    product.Price = dto.Price;
    product.IsActive = dto.IsActive;
    await db.SaveChangesAsync(); // Triggers auto-invalidation
    return Results.Ok(product);
});

// DELETE /products/{id} — deletes single product, auto-invalidates via SaveChanges
app.MapDelete("/products/{id:int}", async (int id, CatalogDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound();

    db.Products.Remove(product);
    await db.SaveChangesAsync(); // Triggers auto-invalidation
    return Results.NoContent();
});

// DELETE /products/bulk?maxPrice=... — ExecuteDelete bypasses SaveChanges,
// so manual invalidation is required
app.MapDelete("/products/bulk", async (
    decimal maxPrice, CatalogDbContext db,
    IStashInvalidator invalidator) =>
{
    var deleted = await db.Products
        .Where(p => p.Price < maxPrice)
        .ExecuteDeleteAsync();

    // Manual invalidation required for ExecuteDelete/ExecuteUpdate
    await invalidator.InvalidateEntitiesAsync(db, [typeof(Product)]);

    return Results.Ok(new { Deleted = deleted });
});

// GET /cache/status — diagnostic endpoint
app.MapGet("/cache/status", () =>
    Results.Ok(new { Status = "Cache active", Timestamp = DateTime.UtcNow }));

// POST /cache/clear — admin endpoint to flush entire cache
app.MapPost("/cache/clear", async (IStashInvalidator invalidator) =>
{
    await invalidator.InvalidateAllAsync();
    return Results.Ok(new { Message = "Cache cleared" });
});

// POST /cache/invalidate/products — manually invalidate just the products cache
app.MapPost("/cache/invalidate/{table}", async (
    string table, IStashInvalidator invalidator) =>
{
    await invalidator.InvalidateTablesAsync([table.ToLowerInvariant()]);
    return Results.Ok(new { Message = $"Cache invalidated for table '{table}'" });
});

app.Run();
