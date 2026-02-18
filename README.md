# Stash.EFCore

[![NuGet](https://img.shields.io/nuget/v/Stash.EFCore.svg)](https://www.nuget.org/packages/Stash.EFCore)
[![Build](https://github.com/cwoodruff/Stash.EFCore/actions/workflows/ci.yml/badge.svg)](https://github.com/cwoodruff/Stash.EFCore/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)](https://dotnet.microsoft.com)

**Add second-level query caching to EF Core in 2 lines of code.**

Stash.EFCore transparently caches EF Core query results and automatically invalidates them when `SaveChanges` modifies related entities. No query rewriting, no manual cache keys, no stale data.

## Quick Start

```bash
dotnet add package Stash.EFCore
```

```csharp
// Register services
services.AddStash();

// Add interceptors to your DbContext
services.AddDbContext<MyDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .UseStash(sp));
```

Cache any query with `.Cached()`:

```csharp
var products = await db.Products
    .Where(p => p.Price > 10)
    .Cached()
    .ToListAsync();
// First call hits the database; subsequent calls served from cache.
// When SaveChanges modifies Products, the cache is automatically invalidated.
```

## Features

### Fluent Per-Query Caching

```csharp
// Cache with default TTL (30 minutes)
var items = await db.Products.Cached().ToListAsync();

// Cache with custom absolute TTL
var items = await db.Products.Cached(TimeSpan.FromMinutes(5)).ToListAsync();

// Cache with absolute + sliding expiration
var items = await db.Products
    .Cached(TimeSpan.FromHours(1), TimeSpan.FromMinutes(10))
    .ToListAsync();

// Cache with a named profile
var items = await db.Products.Cached("hot-data").ToListAsync();

// Opt out of caching (useful with CacheAllQueries)
var items = await db.Products.NoStash().ToListAsync();
```

### Automatic Cache Invalidation

When `SaveChanges` is called, Stash detects which tables were modified and invalidates all cached queries that depend on those tables:

```csharp
db.Products.Add(new Product { Name = "Widget", Price = 9.99m });
await db.SaveChangesAsync();
// All cached queries touching the Products table are now invalidated.
```

### Manual Invalidation

For scenarios not covered by `SaveChanges` (e.g., `ExecuteUpdate`, `ExecuteDelete`, raw SQL):

```csharp
// Inject IStashInvalidator
public class ProductService(MyDbContext db, IStashInvalidator invalidator)
{
    public async Task BulkUpdatePrices()
    {
        await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 1.1m));
        await invalidator.InvalidateTablesAsync(["products"]);
    }
}
```

### Named Profiles

```csharp
services.AddStash(options =>
{
    options.Profiles["hot-data"] = new StashProfile
    {
        Name = "hot-data",
        AbsoluteExpiration = TimeSpan.FromMinutes(2),
        SlidingExpiration = TimeSpan.FromSeconds(30)
    };
    options.Profiles["reference-data"] = new StashProfile
    {
        Name = "reference-data",
        AbsoluteExpiration = TimeSpan.FromHours(24)
    };
});
```

### Cache-All Mode

```csharp
services.AddStash(options =>
{
    options.CacheAllQueries = true;
    options.ExcludedTables.Add("AuditLog");
    options.ExcludedTables.Add("Sessions");
});
// Every SELECT is cached automatically. Use .NoStash() to opt out per-query.
```

### HybridCache (L1 + L2, .NET 9+)

```csharp
services.AddHybridCache();  // configure your IDistributedCache provider
services.AddStashWithHybridCache();
```

### Diagnostics and Observability

```csharp
services.AddStash(options =>
{
    options.OnStashEvent = evt =>
    {
        Console.WriteLine($"[Stash] {evt.EventType}: {evt.CacheKey}");
    };
});

// Real-time statistics via IStashStatistics
app.MapGet("/cache-stats", (IStashStatistics stats) => new
{
    stats.Hits, stats.Misses, stats.HitRate,
    stats.Invalidations, stats.TotalBytesCached
});

// Health check integration
builder.Services.AddHealthChecks().AddStashHealthCheck();
```

## Configuration Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultAbsoluteExpiration` | `TimeSpan` | 30 min | Default TTL for cached queries |
| `DefaultSlidingExpiration` | `TimeSpan?` | `null` | Default sliding expiration (disabled by default) |
| `KeyPrefix` | `string` | `"stash:"` | Prefix for all cache keys |
| `CacheAllQueries` | `bool` | `false` | Cache all SELECT queries automatically |
| `ExcludedTables` | `HashSet<string>` | empty | Tables excluded from CacheAllQueries mode |
| `MaxRowsPerQuery` | `int` | 10,000 | Max rows to cache per query; larger results skip caching |
| `MaxCacheEntrySize` | `long` | 0 (off) | Max bytes per cache entry; 0 disables limit |
| `FallbackToDatabase` | `bool` | `true` | Fall back to DB on cache errors instead of throwing |
| `Profiles` | `Dictionary<string, StashProfile>` | empty | Named caching profiles for per-query settings |
| `OnStashEvent` | `Action<StashEvent>?` | `null` | Callback for cache events (hit, miss, invalidation, etc.) |
| `MinimumHitRatePercent` | `double` | 10.0 | Health check threshold; below this reports Degraded |
| `EnableLogging` | `bool` | `true` | Enable structured logging of cache events |

## Architecture

```
┌─────────────────────────────────────────────────┐
│                  EF Core Query                  │
│         db.Products.Cached().ToListAsync()      │
└────────────────────────┬────────────────────────┘
                         │
             ┌───────────▼──────────┐
             │ StashCommandInterceptor │
             │ (DbCommandInterceptor)  │
             └───────────┬─────────────┘
                         │
              ┌──────────┴──────────┐
              │                     │
       ┌──────▼──────┐      ┌──────▼──────┐
       │  Cache Hit  │      │ Cache Miss  │
       │  Return     │      │ Execute DB, │
       │  cached     │      │ cache result│
       │  reader     │      │ return data │
       └─────────────┘      └──────┬──────┘
                                   │
       ┌───────────────────────────▼──────────────────────────┐
       │                     ICacheStore                      │
       │  ┌─────────────────┐   ┌──────────────────────────┐ │
       │  │ MemoryCacheStore │   │ HybridCacheStore (.NET 9) │ │
       │  │  (IMemoryCache)  │   │ (L1 memory + L2 distrib) │ │
       │  └─────────────────┘   └──────────────────────────┘ │
       └──────────────────────────────────────────────────────┘

            ┌──────────────────────────────────┐
            │  StashInvalidationInterceptor    │
            │  (SaveChangesInterceptor)        │
            │                                  │
            │  SavingChanges → capture tables  │
            │  SavedChanges  → invalidate tags │
            └──────────────────────────────────┘
```

## Comparison

| Feature | Stash.EFCore | EFCoreSecondLevelCacheInterceptor | EF+ Query Cache |
|---------|:-----------:|:---------------------------------:|:---------------:|
| Auto-invalidation on SaveChanges | Yes | Yes | Partial |
| Manual invalidation API | Yes (tables, entities, keys, all) | Limited | Yes |
| HybridCache support (.NET 9+) | Yes | No | No |
| Named profiles | Yes | No | No |
| Per-query TTL | Yes | Yes | Yes |
| Health check integration | Yes | No | No |
| Real-time statistics | Yes | No | Limited |
| Event callback system | Yes | No | No |
| Structured logging | Yes | Yes | No |
| Size/row limits | Yes | No | No |
| Cache-all mode | Yes | Yes | No |
| Fluent API | `.Cached()` / `.NoStash()` | Tag-based | `.FromCache()` |
| Min .NET | 8.0 | Standard 2.0 | 6.0 |
| License | MIT | Apache 2.0 | MIT (free tier) |

## Logging

Stash emits structured log events with dedicated `EventId` values:

| EventId | Name | Description |
|---------|------|-------------|
| 30001 | `Stash.EFCore.CacheHit` | Query served from cache |
| 30002 | `Stash.EFCore.CacheMiss` | Query not found in cache |
| 30003 | `Stash.EFCore.Cached` | Query result stored in cache |
| 30004 | `Stash.EFCore.Invalidated` | Cache entries invalidated |
| 30005 | `Stash.EFCore.Error` | Cache operation error |
| 30006 | `Stash.EFCore.SkippedRows` | Skipped caching (too many rows) |
| 30007 | `Stash.EFCore.SkippedSize` | Skipped caching (entry too large) |
| 30008 | `Stash.EFCore.Excluded` | Skipped caching (excluded table) |
| 30009 | `Stash.EFCore.Fallback` | Fell back to database after cache error |

## Roadmap

- **v0.1.0** (current) - Core interceptors, MemoryCacheStore, HybridCacheStore, `.Cached()` API, auto-invalidation, manual invalidation, named profiles, diagnostics/stats, health check, benchmarks
- **v1.0.0** - Stable API after community feedback + additional database provider testing

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, testing instructions, and PR guidelines.

## License

[MIT](LICENSE)
