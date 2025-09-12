# MeteredMemoryCache: OpenTelemetry-Enabled Memory Cache Metrics

## Overview

The MeteredMemoryCache is a decorator implementation that wraps any `IMemoryCache` to emit standardized metrics for cache operations. This solution addresses the lack of built-in metrics in .NET's memory cache implementations, enabling observability for cache hit rates, miss rates, and eviction patterns through OpenTelemetry integration.

## Problem Statement

Modern applications rely heavily on caching for performance, but .NET's `IMemoryCache` provides no built-in metrics capability. This creates several challenges:

1. **Visibility Gap**: Teams cannot monitor cache effectiveness without custom instrumentation
2. **Performance Debugging**: Identifying cache-related performance issues requires manual logging
3. **Capacity Planning**: No data on eviction patterns makes it difficult to right-size caches
4. **Multi-Cache Scenarios**: Applications with multiple caches lack unified metrics

The dotnet/runtime issue [#67770](https://github.com/dotnet/runtime/issues/67770) acknowledges this gap but hasn't been addressed in the framework.

## Goals

### Primary Goals
- Provide zero-configuration metrics for any `IMemoryCache` implementation
- Emit industry-standard metrics (hits, misses, evictions) via OpenTelemetry
- Maintain performance with minimal overhead (~100ns per operation)
- Support multi-cache scenarios with dimensional metrics

### Non-Goals
- Replacing the underlying cache implementation
- Providing cache statistics beyond basic operational metrics
- Supporting caches other than `IMemoryCache`
- Automatic cache tuning or optimization

## Design Decisions

### 1. Decorator Pattern vs. Inheritance

**Decision**: Use the decorator pattern wrapping `IMemoryCache`

**Rationale**:
- Works with any `IMemoryCache` implementation (MemoryCache, DistributedMemoryCache, custom)
- No breaking changes to existing code
- Clear separation of concerns
- Allows composition with other decorators

**Alternatives Considered**:
- Inheritance: Would only work with specific implementations
- Extension methods: Cannot intercept all operations
- Source generators: Too complex for the use case

### 2. Metrics Collection Approach

**Decision**: Intercept all cache operations and emit counters immediately

**Rationale**:
- Real-time metrics without polling overhead
- Access to operation context (keys, values)
- Supports exemplar collection for debugging
- No dependency on implementation-specific features

**Trade-offs**:
- ~100ns overhead per operation
- Requires wrapping all cache instances
- Cannot retroactively instrument existing caches

### 3. Metric Naming and Structure

**Decision**: Use OpenTelemetry semantic conventions with dimensional tags

```
cache_hits_total{cache.name="user-cache"}
cache_misses_total{cache.name="user-cache"}
cache_evictions_total{cache.name="user-cache", reason="Capacity"}
```

**Rationale**:
- Follows industry standards
- Compatible with Prometheus, Grafana, etc.
- Dimensional metrics enable filtering and aggregation
- Consistent with other OTel instrumentations

## Implementation Architecture

### Core Components

```mermaid
classDiagram
    class IMemoryCache {
        <<interface>>
        +TryGetValue(key, out value)
        +CreateEntry(key)
        +Remove(key)
    }
    
    class MeteredMemoryCache {
        -IMemoryCache inner
        -Counter~long~ hits
        -Counter~long~ misses
        -Counter~long~ evictions
        -TagList tags
        +TryGetValue(key, out value)
        +CreateEntry(key)
        +Remove(key)
        +GetOrCreate(key, factory)
    }
    
    class Meter {
        +CreateCounter(name)
    }
    
    class Counter {
        +Add(value, tags)
    }
    
    IMemoryCache <|.. MeteredMemoryCache
    MeteredMemoryCache --> IMemoryCache : decorates
    MeteredMemoryCache --> Meter : uses
    MeteredMemoryCache --> Counter : emits to
```

### Key Methods

1. **TryGetValue**: Increments hit/miss counters based on result
2. **CreateEntry**: Registers eviction callback to track evictions
3. **GetOrCreate**: Combines hit/miss tracking with eviction registration
4. **Set**: Convenience method with automatic eviction callback

### Eviction Tracking

The implementation hooks into the `PostEvictionCallback` to capture eviction metrics:

```csharp
entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
{
    var self = (MeteredMemoryCache)state!;
    self._evictions.Add(1, new KeyValuePair<string, object?>("reason", reason.ToString()));
}, this);
```

## Usage Examples

### Basic Setup

```csharp
// Configure services
services.AddSingleton<Meter>(sp => new Meter("MyApp.Cache"));
services.AddSingleton<IMemoryCache>(sp =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var meter = sp.GetRequiredService<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "user-cache");
});

// Configure OpenTelemetry
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter());
```

### Multiple Named Caches

```csharp
services.AddNamedMeteredMemoryCache("user-cache", configure: opt => 
{
    opt.SizeLimit = 1000;
});

services.AddNamedMeteredMemoryCache("product-cache", configure: opt => 
{
    opt.SizeLimit = 5000;
});
```

### Decorating Existing Cache

```csharp
services.AddMemoryCache();
services.DecorateMemoryCacheWithMetrics("main-cache");
```

## Performance Characteristics

### Overhead Analysis

| Operation | Raw Cache | Metered Cache | Overhead |
|-----------|-----------|---------------|----------|
| Hit       | ~50ns     | ~150ns        | +100ns   |
| Miss      | ~100ns    | ~200ns        | +100ns   |
| Set       | ~200ns    | ~350ns        | +150ns   |

### Memory Impact

- Per-instance: ~200 bytes (3 counters + tags)
- Per-operation: 0 allocations on hit path
- Per-eviction: 1 allocation for tag array

### Scalability

- Thread-safe via underlying counter implementations
- No global locks or contention points
- Linear scaling with operation rate

## Migration Strategy

### From Unmetered Cache

```csharp
// Before
services.AddMemoryCache();

// After
services.AddMemoryCache();
services.DecorateMemoryCacheWithMetrics("app-cache");
```

### From Custom Metrics

```csharp
// Before
public T GetFromCache<T>(string key)
{
    _customHitCounter.Increment();
    return _cache.Get<T>(key);
}

// After - metrics handled automatically
public T GetFromCache<T>(string key)
{
    return _cache.Get<T>(key);
}
```

## Testing Approach

### Unit Tests
- Verify metric emission for all operations
- Validate eviction callback registration
- Ensure proper tag propagation
- Test disposal behavior

### Integration Tests
- Verify OpenTelemetry integration
- Test multi-cache scenarios
- Validate metric aggregation

### Benchmarks
```csharp
[Benchmark]
public int RawMemoryCache_Hit() => /* ... */

[Benchmark] 
public int Metered_Hit() => /* ... */
```

## Security Considerations

- No sensitive data in metrics (no cache keys/values)
- Cache names should not contain PII
- Metrics follow standard OTel security model
- No additional attack surface

## Future Enhancements

### Phase 1 (Current)
- Basic hit/miss/eviction metrics
- Named cache support
- OpenTelemetry integration

### Phase 2 (Potential)
- Cache size metrics (if accessible)
- Hit rate histograms
- Latency percentiles
- Custom tag providers

### Phase 3 (Long-term)
- Native framework integration
- Automatic cache discovery
- Advanced analytics

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2024-01 | Use decorator pattern | Maximum flexibility, no breaking changes |
| 2024-01 | Emit counters vs observable | Real-time metrics, exemplar support |
| 2024-01 | Support cache naming | Multi-cache scenarios common |
| 2024-01 | Static callbacks | Avoid per-operation allocations |

## References

- [OpenTelemetry Metrics Specification](https://opentelemetry.io/docs/reference/specification/metrics/)
- [.NET Metrics API](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/metrics)
- [Issue #67770: Add metrics to IMemoryCache](https://github.com/dotnet/runtime/issues/67770)
- [Semantic Conventions for Cache Metrics](https://opentelemetry.io/docs/reference/specification/trace/semantic_conventions/cache/)