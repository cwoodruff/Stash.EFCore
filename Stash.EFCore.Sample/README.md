# Stash.EFCore Sample — Product Catalog API

A minimal ASP.NET Core Web API demonstrating all Stash.EFCore features.

## Running

```bash
cd Stash.EFCore.Sample
dotnet run
```

The API starts on `http://localhost:5000` (or the port shown in console output).
A SQLite database (`catalog.db`) is created automatically with seed data:
5 categories, 3 suppliers, and 100 products.

## Endpoints

### Cached Reads

```bash
# Default TTL — .Cached()
curl http://localhost:5000/products

# Explicit TTL — .Cached(TimeSpan)
curl http://localhost:5000/products/1

# Named profile "reference-data" (4-hour TTL) — .Cached("reference-data")
curl http://localhost:5000/categories

# Named profile "hot-data" (1-min TTL) — .Cached("hot-data")
curl http://localhost:5000/suppliers

# Cached search with dynamic filters
curl "http://localhost:5000/products/search?q=Widget&minPrice=10&maxPrice=100"

# Cached aggregation (Count, Average)
curl http://localhost:5000/stats
```

### Writes with Auto-Invalidation

SaveChanges automatically invalidates cached queries for modified tables.

```bash
# Create — invalidates products cache
curl -X POST http://localhost:5000/products \
  -H "Content-Type: application/json" \
  -d '{"name":"New Product","price":29.99,"categoryId":1,"supplierId":1}'

# Update — invalidates products cache
curl -X PUT http://localhost:5000/products/1 \
  -H "Content-Type: application/json" \
  -d '{"name":"Updated Widget","price":19.99,"isActive":true}'

# Delete — invalidates products cache
curl -X DELETE http://localhost:5000/products/100
```

### Bulk Delete with Manual Invalidation

`ExecuteDeleteAsync` bypasses the change tracker, so auto-invalidation
doesn't fire. The endpoint uses `IStashInvalidator` to manually invalidate.

```bash
curl -X DELETE "http://localhost:5000/products/bulk?maxPrice=10"
```

### Excluded Tables

The `AuditLogs` table is in `ExcludedTables`, so it is never cached
even with `CacheAllQueries`. This endpoint always hits the database.

```bash
curl http://localhost:5000/audit-logs
```

### Cache Administration

```bash
# Check cache status
curl http://localhost:5000/cache/status

# Flush entire cache
curl -X POST http://localhost:5000/cache/clear

# Invalidate a specific table's cache entries
curl -X POST http://localhost:5000/cache/invalidate/products
```

## Features Demonstrated

| Feature | Endpoint / Code |
|---|---|
| `.Cached()` default TTL | `GET /products` |
| `.Cached(TimeSpan)` explicit TTL | `GET /products/{id}`, `GET /products/search` |
| `.Cached("profile")` named profiles | `GET /categories`, `GET /suppliers` |
| Scalar caching (Count, Average) | `GET /stats` |
| Auto-invalidation via SaveChanges | `POST /products`, `PUT /products/{id}`, `DELETE /products/{id}` |
| Manual invalidation (`IStashInvalidator`) | `DELETE /products/bulk`, `POST /cache/clear`, `POST /cache/invalidate/{table}` |
| `ExcludedTables` | `AuditLogs` table excluded in config |
| Cache logging | Console shows `StashCacheHit` / `StashCacheMiss` events |

## Observing Cache Behavior

Watch the console output while making requests. You will see log messages like:

```
dbug: Stash.EFCore.Interceptors.StashCommandInterceptor[101]
      Cache miss for key 'stash:...' — executing against database
dbug: Stash.EFCore.Interceptors.StashCommandInterceptor[100]
      Cache hit for key 'stash:...' — returning cached result
```

To verify caching is working, call the same endpoint twice and confirm
the second call shows a cache hit (no SQL executed).
