using BenchmarkDotNet.Running;
using Stash.EFCore.Benchmarks;

BenchmarkRunner.Run<QueryCacheBenchmark>();
