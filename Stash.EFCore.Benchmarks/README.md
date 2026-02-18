# Stash.EFCore Benchmarks

BenchmarkDotNet performance tests measuring the impact of Stash.EFCore query caching.

## Running Benchmarks

```bash
# Run ALL benchmarks (takes 10-20 minutes)
dotnet run --project Stash.EFCore.Benchmarks -c Release

# List available benchmarks
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --list flat

# Run a specific benchmark class
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*CacheHitVsMiss*"
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*CachingOverhead*"
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*ResultSetSizes*"
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*KeyGeneration*"
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*Invalidation*"
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*ConcurrentReads*"

# Quick validation run (fewer iterations)
dotnet run --project Stash.EFCore.Benchmarks -c Release -- --filter "*KeyGeneration*" --job short
```

**Important:** Always run benchmarks in `Release` configuration. Debug builds include
extra allocations and diagnostics that distort results.

## Benchmark Descriptions

### 1. CacheHitVsMiss

Compares a cold query (cache miss, hits SQLite) against a warm query (cache hit,
served from memory). This is the primary benchmark showing the speed benefit of caching.

**Key metric:** Mean execution time ratio between miss and hit.

### 2. CachingOverhead

Measures the overhead Stash.EFCore interceptors add to a cache-miss query compared
to a baseline query with no interceptors. This shows the cost of the interceptor pipeline,
key generation, result set capture, and cache storage on the miss path.

**Key metric:** How many extra nanoseconds/microseconds the miss path adds vs baseline.

### 3. ResultSetSizes

Benchmarks four cache operations across result set sizes (1 to 10,000 rows):

| Operation | What it measures |
|---|---|
| **Capture** | `DbDataReader` → `CacheableResultSet` (reading + schema extraction) |
| **Serialize** | `CacheableResultSet` → `byte[]` (for distributed cache scenarios) |
| **Deserialize** | `byte[]` → `CacheableResultSet` (restoring from distributed cache) |
| **Replay** | `CachedDataReader` full iteration (what EF Core sees on cache hit) |

**Key metric:** How each operation scales with row count.

### 4. KeyGeneration

Benchmarks SHA256-based cache key generation across query complexity:

- Simple query (no parameters)
- 5 parameters
- 20 parameters
- Very long SQL (5000+ characters with many JOINs)

**Key metric:** Nanoseconds per key generation, memory allocated.

### 5. Invalidation

Benchmarks the cost of tag-based cache invalidation:

- 1 table with 10 cached entries
- 1 table with 1,000 cached entries
- 5 tables invalidated simultaneously (100 entries each)

**Key metric:** How invalidation cost scales with entry count.

### 6. ConcurrentReads

Benchmarks throughput under concurrent readers (1, 10, 50, 100) all hitting
the same cached query. Measures total execution time and memory allocation
to verify the cache scales well under contention.

**Key metric:** Total time and per-reader allocation at high concurrency.

## Interpreting Results

BenchmarkDotNet output includes:

| Column | Meaning |
|---|---|
| **Mean** | Average execution time per operation |
| **Error** | Half-width of the 99.9% confidence interval |
| **Ratio** | How this compares to the baseline (1.00 = same) |
| **Rank** | Position among benchmarks in the class (1 = fastest) |
| **Gen0/Gen1** | GC collections per 1,000 operations |
| **Allocated** | Managed memory allocated per operation |

Results are written to `BenchmarkDotNet.Artifacts/` in the working directory.
