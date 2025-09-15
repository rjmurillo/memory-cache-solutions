# Memory Cache Solutions

High‑quality experimental patterns & decorators built on top of `IMemoryCache` ([Microsoft.Extensions.Caching.Memory](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory)) to address common performance and correctness concerns.

## Table of Contents

- [Components Overview](#components-overview)
- [Quick Start](#quick-start)
- [MeteredMemoryCache](#meteredmemorycache)
- [OptimizedMeteredMemoryCache](#optimizedmeteredmemorycache)
- [Implementation Details](#implementation-details)
- [Choosing an Approach](#choosing-an-approach)
- [Benchmarks & Performance](#benchmarks--performance)
- [Documentation](#documentation)
- [Testing](#testing)
- [License](#license)

## Components Overview

| Component                     | Purpose                                                                                      | Concurrency Control                                                           | Async Support              | Extra Features                                                                       |
| ----------------------------- | -------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- | -------------------------- | ------------------------------------------------------------------------------------ |
| `MeteredMemoryCache`          | Emits OpenTelemetry / .NET `System.Diagnostics.Metrics` counters for hits, misses, evictions | Thread-safe counter operations with dimensional tags                          | N/A (sync like base cache) | Named caches, custom tags, service collection extensions, options pattern validation |
| `OptimizedMeteredMemoryCache` | High-performance metrics decorator using atomic operations for minimal overhead              | `Interlocked` atomic operations for counters                                  | N/A (sync like base cache) | Periodic metric publishing, `GetCurrentStatistics()`, &lt;5% performance overhead    |

> These implementations favor clarity & demonstrable patterns over feature breadth. They are intentionally small and suitable as a starting point for production adaptation.

---

## Quick Start

Add the project (or copy the desired file) into your solution and reference it from your application. Example using the metered cache with DI:

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddNamedMeteredMemoryCache("user-cache");
```

## Recommended Alternatives for Single-Flight (Cache Stampede Protection)

For single-flight scenarios, we recommend using these mature, production-ready solutions instead of implementing your own:

### Microsoft HybridCache (.NET 9+)

- **First-party solution** from Microsoft with built-in cache stampede protection
- **L1 + L2 cache support** (in-memory + distributed)
- **Cache invalidation with tags** for bulk operations
- **Simple API** - reduces complex cache-aside patterns to a single line
- **Performance optimizations** including support for `IBufferDistributedCache`
- **Secure by default** with authentication and data handling

```csharp
// Simple usage with HybridCache
public class SomeService(HybridCache cache)
{
    public async Task<SomeInformation> GetSomeInformationAsync(string name, int id, CancellationToken token = default)
    {
        return await cache.GetOrCreateAsync(
            $"someinfo:{name}:{id}",
            async cancel => await SomeExpensiveOperationAsync(name, id, cancel),
            token: token
        );
    }
}
```

### FusionCache (All .NET Versions)

- **Mature OSS library** with comprehensive single-flight support
- **Request coalescing** - only one factory runs per key concurrently
- **Rich feature set**: soft/hard timeouts, fail-safe, eager refresh, backplane
- **Excellent documentation** and active maintenance
- **Supports older .NET versions** down to .NET Framework 4.7.2

```csharp
// Simple usage with FusionCache
public class SomeService(FusionCache cache)
{
    public async Task<SomeInformation> GetSomeInformationAsync(string name, int id, CancellationToken token = default)
    {
        return await cache.GetOrSetAsync(
            $"someinfo:{name}:{id}",
            async cancel => await SomeExpensiveOperationAsync(name, id, cancel),
            TimeSpan.FromMinutes(5),
            token
        );
    }
}
```

### When to Choose Which

- **Greenfield or .NET 9+**: Use **HybridCache** - first-party, GA, built-in stampede protection
- **Need richer features or .NET < 9**: Use **FusionCache** - comprehensive feature set, excellent documentation


Recording metrics with `MeteredMemoryCache`:

```csharp
var meter = new Meter("app.cache");
var metered = new MeteredMemoryCache(new MemoryCache(new MemoryCacheOptions()), meter);

metered.Set("answer", 42);
if (metered.TryGet<int>("answer", out var v)) { /* use v */ }
```

For high-performance scenarios, use `OptimizedMeteredMemoryCache` with atomic operations:

```csharp
var meter = new Meter("app.cache");
var optimized = new OptimizedMeteredMemoryCache(
    new MemoryCache(new MemoryCacheOptions()),
    meter,
    cacheName: "user-cache");

// Get real-time statistics
var stats = optimized.GetCurrentStatistics();
Console.WriteLine($"Hit ratio: {stats.HitRatio:F2}%");

// Periodic metric publishing (call from background service)
optimized.PublishMetrics();
```

Counters exposed:

- `cache_hits_total`
- `cache_misses_total`
- `cache_evictions_total` (tag: `reason` = `Expired|TokenExpired|Capacity|Removed|Replaced|...`)

Consume with `MeterListener`, OpenTelemetry Metrics SDK, or any compatible exporter.

---

## MeteredMemoryCache

The `MeteredMemoryCache` provides comprehensive observability for cache operations through OpenTelemetry metrics integration. It decorates any `IMemoryCache` implementation with zero-configuration metrics emission.

### Quick Setup

```csharp
// Register with dependency injection
builder.Services.AddNamedMeteredMemoryCache("user-cache");

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("CacheImplementations.MeteredMemoryCache")
        .AddOtlpExporter());
```

### Key Features

- **Named Cache Support**: Dimensional metrics with `cache.name` tags
- **Service Collection Extensions**: Easy DI integration
- **Options Pattern**: Configurable behavior with validation
- **Minimal Overhead**: 15-40ns per operation
- **Thread-Safe**: Lock-free counter operations

### Emitted Metrics

| Metric                  | Description                 | Tags                   |
| ----------------------- | --------------------------- | ---------------------- |
| `cache_hits_total`      | Successful cache retrievals | `cache.name`           |
| `cache_misses_total`    | Cache key not found         | `cache.name`           |
| `cache_evictions_total` | Items removed from cache    | `cache.name`, `reason` |

For detailed usage, configuration, and examples, see the [MeteredMemoryCache Usage Guide](docs/MeteredMemoryCache.md).

---

## OptimizedMeteredMemoryCache

The `OptimizedMeteredMemoryCache` is a high-performance alternative to `MeteredMemoryCache` that uses atomic operations (`Interlocked`) instead of `Counter<T>` for minimal overhead. Inspired by the performance patterns used in `HybridCache` and `MemoryCache.GetCurrentStatistics()`.

### OptimizedMeteredMemoryCache Performance Benefits

- **Ultra-low overhead**: &lt;5% performance impact vs raw `MemoryCache`
- **Atomic operations**: Uses `Interlocked.Increment` for thread-safe counting
- **Periodic publishing**: Batches metric emission to reduce per-operation cost
- **Real-time statistics**: `GetCurrentStatistics()` method for immediate metrics access

### OptimizedMeteredMemoryCache Quick Setup

```csharp
var meter = new Meter("app.cache");
var optimized = new OptimizedMeteredMemoryCache(
    new MemoryCache(new MemoryCacheOptions()),
    meter,
    cacheName: "user-cache",
    enableMetrics: true);

// Get real-time statistics
var stats = optimized.GetCurrentStatistics();
Console.WriteLine($"Hit ratio: {stats.HitRatio:F2}%");

// Periodic metric publishing (call from background service)
optimized.PublishMetrics();
```

### OptimizedMeteredMemoryCache Key Features

- **Atomic Counters**: `Interlocked` operations for minimal overhead
- **Periodic Publishing**: `PublishMetrics()` method for batched metric emission
- **Real-time Statistics**: `GetCurrentStatistics()` for immediate metrics access
- **Optional Metrics**: Can disable metrics entirely for maximum performance
- **Thread-Safe**: Lock-free atomic operations

### When to Use OptimizedMeteredMemoryCache

- **High-throughput scenarios**: When cache operations are in the critical path
- **Performance-sensitive applications**: Where every nanosecond matters
- **Real-time monitoring**: When you need immediate access to cache statistics
- **Background metric publishing**: When you can batch metric emission

### Performance Comparison

Based on benchmarks, `OptimizedMeteredMemoryCache` shows:

- **25-63ns per operation** (vs higher overhead with `Counter<T>`)
- **Minimal memory allocation** during cache operations
- **Competitive with FastCache** for high-performance scenarios

For detailed performance analysis, see [Performance Optimization Recommendations](docs/PerformanceOptimizationRecommendations.md).

---

## Implementation Details & Semantics

### MeteredMemoryCache Implementation

- Adds minimal instrumentation overhead (~1 counter add per op) while preserving `IMemoryCache` API.
- Eviction metric is emitted from a post‑eviction callback automatically registered on each created entry.
- Includes convenience `TryGet<T>` & `GetOrCreate<T>` wrappers emitting structured counters.
- Use when you need visibility (hit ratio, churn) without adopting a full external caching layer.

### OptimizedMeteredMemoryCache Implementation

- High-performance alternative using atomic operations (`Interlocked.Increment`) instead of `Counter<T>`.
- Provides `GetCurrentStatistics()` method for real-time metrics access, similar to `MemoryCache.GetCurrentStatistics()`.
- Supports periodic metric publishing via `PublishMetrics()` to reduce per-operation overhead.
- Can disable metrics entirely (`enableMetrics: false`) for maximum performance scenarios.
- Use when performance is critical and you can batch metric emission or need real-time statistics.


---

## Choosing an Approach

| Scenario                                                                                   | Recommended                                                                               |
| ------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------- |
| Need metrics (hit ratio, eviction reasons) with minimal overhead                           | `MeteredMemoryCache`                                                                      |
| Need metrics with ultra-low overhead (&lt;5% impact) or real-time statistics               | `OptimizedMeteredMemoryCache` (atomic operations, periodic publishing)                    |
| Need single-flight (cache stampede protection) for .NET 9+                                 | **[Microsoft HybridCache](https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga)** |
| Need single-flight with richer features or .NET < 9                                        | **[FusionCache](https://github.com/ZiggyCreatures/FusionCache)**                          |

---

## Concurrency, Cancellation & Failure Notes

| Component                   | Cancellation Behavior                                                                                                             | Failure Behavior                                                                                                              |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| MeteredMemoryCache          | N/A (no async).                                                                                                                   | Eviction reasons recorded regardless.                                                                                         |
| OptimizedMeteredMemoryCache | N/A (no async).                                                                                                                   | Eviction reasons recorded regardless; atomic counters remain consistent.                                                      |
| HybridCache                 | See [HybridCache documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-9.0)     | See [HybridCache documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-9.0) |
| FusionCache                 | See [FusionCache documentation](https://github.com/ZiggyCreatures/FusionCache)                                                    | See [FusionCache documentation](https://github.com/ZiggyCreatures/FusionCache)                                                |

---

## Benchmarks

Benchmarks (BenchmarkDotNet) included under `tests/Benchmarks` compare relative overhead of wrappers. To run:

```bash
dotnet run -c Release -p tests/Benchmarks/Benchmarks.csproj
```

Interpretation guidance:

- `OptimizedMeteredMemoryCache` shows &lt;5% overhead vs raw `MemoryCache` (25-63ns per operation).
- `MeteredMemoryCache` shows higher overhead due to `Counter<T>` operations.

> Always benchmark within your workload; microbenchmarks do not capture memory pressure, GC, or production contention levels.

---

## Benchmark Regression Gate (BenchGate)

The repository includes a lightweight regression gate comparing the latest BenchmarkDotNet run against committed baselines.

Quick local workflow:

```powershell
dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *CacheBenchmarks*
Copy-Item BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json BenchmarkDotNet.Artifacts/results/current.json
dotnet run -c Release --project tools/BenchGate/BenchGate.csproj -- benchmarks/baseline/CacheBenchmarks.json BenchmarkDotNet.Artifacts/results/current.json
```

Thresholds (defaults):

- Time regression: >3% AND >5 ns absolute
- Allocation regression: increase >16 B AND >3%

Update baseline only after a verified improvement:

```powershell
Copy-Item BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json benchmarks/baseline/CacheBenchmarks.json
git add benchmarks/baseline/CacheBenchmarks.json
git commit -m "chore(bench): update CacheBenchmarks baseline" -m "Include before/after metrics table"
```

CI runs the gate automatically (see `.github/workflows/ci.yml`).

### BenchGate Regression Gating

BenchGate compares the latest BenchmarkDotNet full JSON output(s) against committed baselines under `benchmarks/baseline/`.

Supported CLI flags:

- `--suite=<SuiteName>`: Explicit suite name if not inferrable.
- `--time-threshold=<double>`: Relative mean time regression guard (default 0.03).
- `--alloc-threshold-bytes=<int>`: Absolute allocation regression guard (default 16).
- `--alloc-threshold-pct=<double>`: Relative allocation regression guard (default 0.03).
- `--sigma-mult=<double>`: Sigma multiplier for statistical significance (default 2.0).
- `--no-sigma`: Disable significance filtering (treat all deltas as significant subject to thresholds).

Per‑OS baseline resolution order when first argument is a directory:

1. `<Suite>.<os>.<arch>.json`
2. `<Suite>.<os>.json`
3. `<Suite>.json`

Current baselines (Windows):

- `CacheBenchmarks.windows-latest.json`
- `ContentionBenchmarks.windows-latest.json`

Add additional OS baselines by copying the corresponding `*-report-full.json` into the baseline directory using the naming convention above.

Evidence & Process requirements are described in `.github/copilot-instructions.md` Sections 12–14.

---

## Extensibility Ideas

- Enrich metrics (e.g., object size, latency histogram for factory execution).
- Add negative caching (cache specific failures briefly) if upstream calls are very costly.
- Provide a multi-layer (L1 in-memory + L2 distributed) single-flight composition.

---

## Documentation

Comprehensive guides and references are available in the `docs/` directory:

### Usage Guides

- [MeteredMemoryCache Usage Guide](docs/MeteredMemoryCache.md) - Complete usage documentation with examples
- [OpenTelemetry Integration](docs/OpenTelemetryIntegration.md) - Setup guide for various OTel exporters
- [Multi-Cache Scenarios](docs/MultiCacheScenarios.md) - Patterns for managing multiple named caches

### Reference Documentation

- [Performance Characteristics](docs/PerformanceCharacteristics.md) - Detailed benchmark analysis and optimization guidance
- [Troubleshooting Guide](docs/Troubleshooting.md) - Common issues and solutions
- [API Reference](docs/ApiReference.md) - Complete API documentation with examples

### Quick Reference Links

- **Getting Started**: See [Quick Start](#quick-start) above
- **Performance Impact**: [Performance Characteristics](docs/PerformanceCharacteristics.md)
- **Common Issues**: [Troubleshooting Guide](docs/Troubleshooting.md)
- **Advanced Patterns**: [Multi-Cache Scenarios](docs/MultiCacheScenarios.md)

---

## Testing

Unit tests cover: metrics emission, cache operations, and thread safety. See the `tests/Unit` directory for usage patterns.

---

## License

MIT (see `LICENSE.txt`).

---

## Disclaimer

These are illustrative implementations. Review thread-safety, memory usage, eviction policies, and failure handling for your production context (high cardinality keys, very large payloads, process restarts, etc.).
