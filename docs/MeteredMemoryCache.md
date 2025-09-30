# MeteredMemoryCache Usage Guide

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Basic Usage](#basic-usage)
- [Advanced Configuration](#advanced-configuration)
- [API Reference](#api-reference)
- [Best Practices](#best-practices)
- [Migration Guide](#migration-guide)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

## Overview

MeteredMemoryCache is a decorator for `IMemoryCache` that automatically emits OpenTelemetry metrics for cache operations. It provides observability into cache hit rates, miss rates, and eviction patterns without requiring changes to your existing cache usage code.

### Key Features

- **Zero-configuration metrics** for any `IMemoryCache` implementation
- **OpenTelemetry integration** with standardized metric names
- **Dimensional metrics** with cache naming and custom tags
- **Minimal performance overhead** (15-40ns per operation)
- **Thread-safe** operations with concurrent metric collection
- **Dependency injection support** with .NET options pattern

### Emitted Metrics

| Metric Name             | Type    | Description                        | Tags                              |
| ----------------------- | ------- | ---------------------------------- | --------------------------------- |
| `cache_hits_total`      | Counter | Number of successful cache lookups | `cache.name` (optional)           |
| `cache_misses_total`    | Counter | Number of failed cache lookups     | `cache.name` (optional)           |
| `cache_evictions_total` | Counter | Number of cache evictions          | `cache.name` (optional), `reason` |

#### Eviction Reasons

The `reason` tag on `cache_evictions_total` corresponds to `EvictionReason` enum values:

- `None` - Not evicted
- `Removed` - Explicitly removed
- `Replaced` - Replaced by newer entry
- `Expired` - Expired based on time
- `TokenExpired` - Expired based on cancellation token
- `Capacity` - Evicted due to cache size limits

## Quick Start

### Dependency Injection (Recommended)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CacheImplementations;

var builder = Host.CreateApplicationBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddPrometheusExporter());

// Register named cache with metrics
builder.Services.AddNamedMeteredMemoryCache("user-cache");

var app = builder.Build();

// Use the cache - metrics are emitted automatically
var cache = app.Services.GetRequiredKeyedService<IMemoryCache>("user-cache");
var result = cache.Get("some-key");
```

### Manual Setup

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;

// Create and wrap cache
var innerCache = new MemoryCache(new MemoryCacheOptions());
var meter = new Meter("MyApp.Cache");
IMemoryCache cache = new MeteredMemoryCache(innerCache, meter, "my-cache");

// Use normally - metrics emitted automatically
cache.Set("key", "value");
var value = cache.Get("key");
```

## Basic Usage

### Manual Instantiation

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;

// Create the underlying cache and meter
var innerCache = new MemoryCache(new MemoryCacheOptions
{
    SizeLimit = 1000
});
var meter = new Meter("MyApp.Cache");

// Wrap with MeteredMemoryCache
IMemoryCache cache = new MeteredMemoryCache(innerCache, meter, "user-cache");

// Use normally - metrics are emitted automatically
var user = cache.GetOrCreate("user:123", entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
    return LoadUserFromDatabase(123);
});

// Clean up
cache.Dispose();
meter.Dispose();
```

### With Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CacheImplementations;

var builder = Host.CreateApplicationBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddPrometheusExporter());

// Register named cache with metrics
builder.Services.AddNamedMeteredMemoryCache("user-cache", options =>
{
    options.AdditionalTags["service"] = "user-service";
    options.AdditionalTags["version"] = "1.0";
});

var app = builder.Build();

// Resolve and use the cache
var cache = app.Services.GetRequiredKeyedService<IMemoryCache>("user-cache");
var result = cache.Get("some-key");
```

## Advanced Configuration

### Using Options Pattern

```csharp
var options = new MeteredMemoryCacheOptions
{
    CacheName = "product-cache",
    DisposeInner = true,
    AdditionalTags =
    {
        ["environment"] = "production",
        ["datacenter"] = "us-west-2",
        ["team"] = "catalog"
    }
};

var cache = new MeteredMemoryCache(innerCache, meter, options);
```

### Multiple Named Caches

```csharp
// Register multiple caches with different configurations
services.AddNamedMeteredMemoryCache("user-cache", opts =>
{
    opts.AdditionalTags["type"] = "user-data";
});

services.AddNamedMeteredMemoryCache("product-cache", opts =>
{
    opts.AdditionalTags["type"] = "product-data";
});

services.AddNamedMeteredMemoryCache("session-cache", opts =>
{
    opts.AdditionalTags["type"] = "session-data";
    opts.AdditionalTags["ttl"] = "short";
});

// Resolve specific caches
var userCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("user-cache");
var productCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("product-cache");
```

### Decorating Existing Cache Registration

```csharp
// Add basic memory cache
services.AddMemoryCache();

// Decorate with metrics
services.DecorateMemoryCacheWithMetrics("main-cache",
    meterName: "MyApp.Cache",
    configureOptions: opts =>
    {
        opts.AdditionalTags["component"] = "main";
    });
```

## API Reference

### Constructors

#### Primary Constructor

```csharp
public MeteredMemoryCache(
    IMemoryCache innerCache,
    Meter meter,
    string? cacheName = null,
    bool disposeInner = false)
```

- `innerCache`: The underlying cache implementation to decorate
- `meter`: OpenTelemetry meter for metric emission
- `cacheName`: Optional logical name for dimensional metrics
- `disposeInner`: Whether to dispose the inner cache on disposal

#### Options Constructor

```csharp
public MeteredMemoryCache(
    IMemoryCache innerCache,
    Meter meter,
    MeteredMemoryCacheOptions options)
```

### Using Extension Methods

`MeteredMemoryCache` implements the [`IMemoryCache`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.imemorycache?view=net-9.0-pp) interface and works with all standard extension methods from `Microsoft.Extensions.Caching.Memory.CacheExtensions`. All operations automatically emit metrics.

**Common Extension Methods:**

- `TryGetValue<T>(object key, out T value)` - Type-safe retrieval with automatic hit/miss metrics
- `Set<T>(object key, T value, MemoryCacheEntryOptions? options)` - Sets entry with automatic eviction tracking
- `GetOrCreate<T>(object key, Func<ICacheEntry, T> factory)` - Gets existing or creates new with full metric coverage
- `GetOrCreateAsync<T>(object key, Func<ICacheEntry, Task<T>> factory)` - Async version of GetOrCreate

**Example:**

```csharp
using Microsoft.Extensions.Caching.Memory;

// Use extension methods directly
if (cache.TryGetValue<UserData>("user:123", out var user))
{
    // Hit recorded automatically
}

cache.Set("user:123", userData, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
});

var result = cache.GetOrCreate($"product:{id}", entry =>
{
    entry.SlidingExpiration = TimeSpan.FromMinutes(10);
    return GetProductFromDatabase(id);
});
```

### Properties

#### Name

```csharp
public string? Name { get; }
```

Gets the logical cache name if provided during construction.

## Extension Methods

### AddNamedMeteredMemoryCache

```csharp
public static IServiceCollection AddNamedMeteredMemoryCache(
    this IServiceCollection services,
    string cacheName,
    Action<MeteredMemoryCacheOptions>? configureOptions = null,
    string? meterName = null)
```

Registers a named cache with complete dependency injection setup including:

- Options validation with `IValidateOptions<T>`
- Keyed service registration for multi-cache scenarios
- Automatic meter registration
- Fallback singleton registration for single-cache scenarios

### DecorateMemoryCacheWithMetrics

```csharp
public static IServiceCollection DecorateMemoryCacheWithMetrics(
    this IServiceCollection services,
    string? cacheName = null,
    string? meterName = null,
    Action<MeteredMemoryCacheOptions>? configureOptions = null)
```

Decorates existing `IMemoryCache` registration with metrics support.

## Best Practices

### 1. Cache Naming Strategy

Use hierarchical naming for related caches:

```csharp
services.AddNamedMeteredMemoryCache("user.profile");
services.AddNamedMeteredMemoryCache("user.permissions");
services.AddNamedMeteredMemoryCache("product.catalog");
services.AddNamedMeteredMemoryCache("product.pricing");
```

### 2. Consistent Tagging

Establish consistent tag naming across your application:

```csharp
var commonOptions = new Action<MeteredMemoryCacheOptions>(opts =>
{
    opts.AdditionalTags["service"] = "my-service";
    opts.AdditionalTags["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
    opts.AdditionalTags["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
});

services.AddNamedMeteredMemoryCache("cache1", commonOptions);
services.AddNamedMeteredMemoryCache("cache2", commonOptions);
```

### 3. Disposal Handling

Configure `DisposeInner` based on ownership:

```csharp
// Own the inner cache - dispose it
services.AddNamedMeteredMemoryCache("owned-cache", opts =>
{
    opts.DisposeInner = true;
});

// Shared cache - don't dispose
services.DecorateMemoryCacheWithMetrics("shared-cache", opts =>
{
    opts.DisposeInner = false; // default
});
```

### 4. Validation Configuration

Always validate options in production:

```csharp
services.AddNamedMeteredMemoryCache("critical-cache", opts =>
{
    // Configuration here
})
.ValidateDataAnnotations()
.ValidateOnStart(); // Fail fast on startup
```

## Performance Characteristics

### Overhead Measurements

Based on benchmarks with 16,384 operations on Windows 11/.NET 9.0.8:

| Operation        | Raw Cache | Metered Cache | Overhead         |
| ---------------- | --------- | ------------- | ---------------- |
| Hit (Get)        | 68.07ns   | 90.77ns       | +22.70ns (+33%)  |
| Miss (Get)       | 52.30ns   | 92.92ns       | +40.62ns (+78%)  |
| Set              | 543.34ns  | 551.03ns      | +7.69ns (+1.4%)  |
| TryGetValue Hit  | 53.35ns   | 61.52ns       | +8.17ns (+15%)   |
| TryGetValue Miss | 43.13ns   | 83.29ns       | +40.16ns (+93%)  |
| CreateEntry      | 537.14ns  | 547.98ns      | +10.84ns (+2.0%) |

### Memory Impact

- **Per-instance**: ~200 bytes (3 counters + tag storage)
- **Per-operation**: 0 allocations on hot path
- **Per-eviction**: 1 allocation for eviction tag array

### Scalability

- Thread-safe via underlying OpenTelemetry counter implementations
- No global locks or contention points
- Linear scaling with operation rate
- Suitable for high-throughput scenarios

## Troubleshooting

### Common Issues

#### 1. No Metrics Appearing

```csharp
// Ensure meter is registered and exported
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache") // Must match meter name
        .AddConsoleExporter()); // Add exporter
```

#### 2. Duplicate Cache Names

```csharp
// Will throw on second registration
services.AddNamedMeteredMemoryCache("duplicate");
services.AddNamedMeteredMemoryCache("duplicate"); // ❌ Throws

// Use different names
services.AddNamedMeteredMemoryCache("cache-v1");
services.AddNamedMeteredMemoryCache("cache-v2"); // ✅ OK
```

#### 3. Missing Eviction Metrics

Eviction metrics are only emitted when:

- Entry has an eviction callback registered (automatic with MeteredMemoryCache)
- Entry is actually evicted (not just expired and accessed)
- Cache is not disposed before eviction occurs

#### 4. Options Validation Failures

```csharp
// ❌ Invalid - null additional tags
var options = new MeteredMemoryCacheOptions
{
    AdditionalTags = null // Throws on validation
};

// ✅ Valid
var options = new MeteredMemoryCacheOptions
{
    AdditionalTags = new Dictionary<string, object?>()
};
```

### Debugging Metrics

#### View Raw Metrics

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddConsoleExporter()); // Prints to console
```

#### Custom Metric Collection

```csharp
using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter("MyApp.Cache")
    .AddInMemoryExporter(exportedItems)
    .Build();

// Perform cache operations
cache.Get("test-key");

// Inspect collected metrics
foreach (var metric in exportedItems)
{
    Console.WriteLine($"{metric.Name}: {metric.GetGaugeLastValueLong()}");
}
```

## Migration Guide

### From Manual Metrics

If you're currently instrumenting cache operations manually, MeteredMemoryCache eliminates the need for custom instrumentation code.

#### Before: Manual Instrumentation

```csharp
public class CacheService
{
    private readonly IMemoryCache _cache;
    private readonly IMetrics _metrics;

    public T Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var value))
        {
            _metrics.Increment("cache.hits");
            return (T)value;
        }
        _metrics.Increment("cache.misses");
        return default(T);
    }

    public void Set<T>(string key, T value)
    {
        _cache.Set(key, value);
        _metrics.Increment("cache.sets");
    }
}
```

#### After: Automatic Instrumentation

```csharp
public class CacheService
{
    private readonly IMemoryCache _cache; // MeteredMemoryCache via DI

    public T Get<T>(string key)
    {
        return _cache.TryGetValue(key, out var value) ? (T)value : default(T);
        // Metrics emitted automatically
    }

    public void Set<T>(string key, T value)
    {
        _cache.Set(key, value);
        // Metrics emitted automatically
    }
}
```

**Migration Steps:**

1. Remove manual metric instrumentation code
2. Register MeteredMemoryCache in DI container
3. Configure OpenTelemetry to collect the new metrics
4. Update monitoring dashboards to use new metric names

### From Custom Cache Wrapper

If you've built a custom cache wrapper for metrics, MeteredMemoryCache provides a standardized replacement.

#### Before: Custom Wrapper

```csharp
public class InstrumentedCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly Counter<long> _hitCounter;
    private readonly Counter<long> _missCounter;

    public InstrumentedCache(IMemoryCache inner, IMeterFactory meterFactory)
    {
        _inner = inner;
        var meter = meterFactory.Create("MyApp.Cache");
        _hitCounter = meter.CreateCounter<long>("cache_hits");
        _missCounter = meter.CreateCounter<long>("cache_misses");
    }

    public bool TryGetValue(object key, out object? value)
    {
        var result = _inner.TryGetValue(key, out value);
        if (result)
            _hitCounter.Add(1);
        else
            _missCounter.Add(1);
        return result;
    }

    // ... implement all other IMemoryCache methods
}
```

#### After: Use MeteredMemoryCache

```csharp
// Register in DI
services.AddNamedMeteredMemoryCache("my-cache");

// Use directly - no custom wrapper needed
public class MyService
{
    private readonly IMemoryCache _cache;

    public MyService(IMemoryCache cache) // or keyed service for named caches
    {
        _cache = cache;
    }

    // All methods instrumented automatically
}
```

### From Other Caching Libraries

#### From Microsoft.Extensions.Caching.Distributed

```csharp
// Before: IDistributedCache (if using in-memory)
services.AddDistributedMemoryCache();

// After: MeteredMemoryCache with equivalent functionality
services.AddNamedMeteredMemoryCache("distributed-equivalent", opts =>
{
    opts.AdditionalTags["cache_type"] = "distributed_memory";
});
```

#### From Third-Party Libraries

```csharp
// Before: LazyCache
services.AddLazyCache();

// After: MeteredMemoryCache with lazy loading pattern
services.AddNamedMeteredMemoryCache("lazy-cache");

// Usage with lazy loading
var result = cache.GetOrCreate("key", entry =>
{
    entry.SlidingExpiration = TimeSpan.FromMinutes(5);
    return ExpensiveOperation();
});
```

### Migration Checklist

- [ ] **Identify Current Metrics**: Document existing cache metrics and naming conventions
- [ ] **Plan Metric Mapping**: Map old metric names to new OpenTelemetry standard names
- [ ] **Update Dependencies**: Add required packages (OpenTelemetry, MeteredMemoryCache)
- [ ] **Configure Services**: Replace cache registrations with MeteredMemoryCache
- [ ] **Remove Custom Code**: Delete manual instrumentation and wrapper classes
- [ ] **Update Monitoring**: Modify dashboards and alerts for new metric names
- [ ] **Test Thoroughly**: Verify metrics are correctly emitted in all scenarios
- [ ] **Monitor Performance**: Ensure acceptable overhead in production workloads

### Metric Name Migration

| Old Metric        | New Metric              | Notes                                        |
| ----------------- | ----------------------- | -------------------------------------------- |
| `cache.hits`      | `cache_hits_total`      | Counter, follows OTel conventions            |
| `cache.misses`    | `cache_misses_total`    | Counter, follows OTel conventions            |
| `cache.sets`      | N/A                     | Tracked via eviction callbacks instead       |
| `cache.evictions` | `cache_evictions_total` | Counter with `reason` tag                    |
| `cache.size`      | N/A                     | Not available through IMemoryCache interface |

### Performance Migration Notes

MeteredMemoryCache adds minimal overhead (15-40ns per operation). If you're migrating from:

- **Heavy custom instrumentation**: Expect performance improvement
- **No instrumentation**: Expect small performance decrease
- **Third-party wrappers**: Performance impact varies by implementation

## Related Documentation

- [OpenTelemetry Integration Guide](OpenTelemetryIntegration.md)
- [MeteredMemoryCache API Reference](../src/CacheImplementations/MeteredMemoryCache.cs)
- [Service Collection Extensions](../src/CacheImplementations/ServiceCollectionExtensions.cs)
- [Options Configuration](../src/CacheImplementations/MeteredMemoryCacheOptions.cs)
