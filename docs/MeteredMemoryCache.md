# MeteredMemoryCache Usage Guide

## Overview

MeteredMemoryCache is a decorator for `IMemoryCache` that automatically emits OpenTelemetry metrics for cache operations. It provides observability into cache hit rates, miss rates, and eviction patterns without requiring changes to your existing cache usage code.

## Key Features

- **Zero-configuration metrics** for any `IMemoryCache` implementation
- **OpenTelemetry integration** with standardized metric names
- **Dimensional metrics** with cache naming and custom tags
- **Minimal performance overhead** (~100ns per operation)
- **Thread-safe** operations with concurrent metric collection
- **Dependency injection support** with .NET options pattern

## Emitted Metrics

| Metric Name | Type | Description | Tags |
|-------------|------|-------------|------|
| `cache_hits_total` | Counter | Number of successful cache lookups | `cache.name` (optional) |
| `cache_misses_total` | Counter | Number of failed cache lookups | `cache.name` (optional) |
| `cache_evictions_total` | Counter | Number of cache evictions | `cache.name` (optional), `reason` |

### Eviction Reasons

The `reason` tag on `cache_evictions_total` corresponds to `EvictionReason` enum values:
- `None` - Not evicted
- `Removed` - Explicitly removed
- `Replaced` - Replaced by newer entry
- `Expired` - Expired based on time
- `TokenExpired` - Expired based on cancellation token
- `Capacity` - Evicted due to cache size limits

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

### Key Methods

#### TryGet<T>
```csharp
public bool TryGet<T>(object key, out T value)
```
Strongly typed retrieval with automatic hit/miss metric emission.

#### Set<T>
```csharp
public void Set<T>(object key, T value, MemoryCacheEntryOptions? options = null)
```
Sets a cache entry with automatic eviction metric registration.

#### GetOrCreate<T>
```csharp
public T GetOrCreate<T>(object key, Func<ICacheEntry, T> factory)
```
Gets existing entry or creates new one with full metric coverage.

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
Based on benchmarks with 16,384 operations:

| Operation | Raw Cache | Metered Cache | Overhead |
|-----------|-----------|---------------|----------|
| Hit (Get) | ~9.5ns | ~19.1ns | ~9.6ns (2.0x) |
| Miss (Get) | ~15.8ns | ~31.6ns | ~15.8ns (2.0x) |
| Set | ~375ns | ~484ns | ~109ns (1.3x) |
| TryGetValue Hit | ~4.5ns | ~5.4ns | ~0.9ns (1.2x) |
| TryGetValue Miss | ~4.3ns | ~6.3ns | ~2.0ns (1.5x) |
| CreateEntry | ~353ns | ~467ns | ~114ns (1.3x) |

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

## Migration Examples

### From Manual Metrics

```csharp
// Before: Manual instrumentation
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
}

// After: Automatic instrumentation
public class CacheService
{
    private readonly IMemoryCache _cache; // MeteredMemoryCache
    
    public T Get<T>(string key)
    {
        return _cache.TryGetValue(key, out var value) ? (T)value : default(T);
        // Metrics emitted automatically
    }
}
```

### From Custom Cache Wrapper

```csharp
// Before: Custom wrapper
public class InstrumentedCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly Counter<long> _hitCounter;
    
    // ... manual implementation of all methods
}

// After: Use MeteredMemoryCache
IMemoryCache cache = new MeteredMemoryCache(innerCache, meter, "my-cache");
// All methods instrumented automatically
```

## Related Documentation

- [OpenTelemetry Integration Guide](OpenTelemetryIntegration.md)
- [MeteredMemoryCache API Reference](../src/CacheImplementations/MeteredMemoryCache.cs)
- [Service Collection Extensions](../src/CacheImplementations/ServiceCollectionExtensions.cs)
- [Options Configuration](../src/CacheImplementations/MeteredMemoryCacheOptions.cs)
