using Microsoft.EntityFrameworkCore;
using Stash.EFCore.Configuration;
using Stash.EFCore.Extensions;
using Stash.EFCore.Sample.Data;

var builder = WebApplication.CreateBuilder(args);

// Register Stash with default in-memory caching
builder.Services.AddStash(options =>
{
    options.DefaultAbsoluteExpiration = TimeSpan.FromMinutes(10);
    options.EnableLogging = true;
});

builder.Services.AddDbContext<SampleDbContext>((sp, options) =>
{
    options.UseSqlite("Data Source=sample.db");

    // Add Stash interceptors to the DbContext pipeline
    var commandInterceptor = sp.GetRequiredService<Stash.EFCore.Interceptors.StashCommandInterceptor>();
    var invalidationInterceptor = sp.GetRequiredService<Stash.EFCore.Interceptors.StashInvalidationInterceptor>();
    options.UseStash(commandInterceptor, invalidationInterceptor);
});

var app = builder.Build();

// Ensure database is created with seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureCreated();
}

// GET /products — cached query using .Cached()
app.MapGet("/products", async (SampleDbContext db) =>
    await db.Products
        .Include(p => p.Category)
        .Cached(TimeSpan.FromMinutes(5))
        .ToListAsync());

// GET /products/{id} — single product, cached
app.MapGet("/products/{id:int}", async (int id, SampleDbContext db) =>
    await db.Products
        .Include(p => p.Category)
        .Where(p => p.Id == id)
        .Cached()
        .FirstOrDefaultAsync()
    is { } product
        ? Results.Ok(product)
        : Results.NotFound());

// POST /products — creates a product, which triggers cache invalidation via SaveChanges
app.MapPost("/products", async (SampleDbContext db, Stash.EFCore.Sample.Models.Product product) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"/products/{product.Id}", product);
});

app.Run();
