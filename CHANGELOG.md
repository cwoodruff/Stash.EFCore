# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-02-18

### Added

- Transparent second-level query cache via EF Core `DbCommandInterceptor`
- Fluent `.Cached()` and `.Cached(TimeSpan)` extension methods on `IQueryable<T>`
- `StashProfile` for named, reusable cache configurations
- Automatic cache invalidation on `SaveChanges` via `DbCommandInterceptor`
- Manual invalidation API (`IStashInvalidator`) with table, tag, and key-based eviction
- `MemoryCacheStore` backed by `IMemoryCache`
- `HybridCacheStore` backed by `Microsoft.Extensions.Caching.Hybrid` (.NET 9+)
- SHA256-based cache key generation with pluggable `ICacheKeyGenerator`
- SQL table-dependency parser for automatic invalidation tracking
- `IStashStatistics` for real-time cache metrics (hits, misses, hit rate, bytes cached)
- `StashHealthCheck` for ASP.NET Core health check integration
- `StashEvent` callback system for observability hooks
- Structured logging with dedicated `EventId` values (30001-30009)
- Multi-target support: net8.0 and net9.0
- Source Link and deterministic builds
- BenchmarkDotNet performance test suite
- GitHub Actions CI/CD with matrix builds, release automation, and benchmark runner

[Unreleased]: https://github.com/cwoodruff/Stash.EFCore/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/cwoodruff/Stash.EFCore/releases/tag/v0.1.0
