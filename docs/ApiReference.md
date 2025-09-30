# MeteredMemoryCache API Reference

This document provides comprehensive API reference documentation for all public classes, methods, and options in the MeteredMemoryCache library.

## Table of Contents

1. [MeteredMemoryCache Class](#meteredmemorycache-class)
2. [MeteredMemoryCacheOptions Class](#meteredmemorycacheoptions-class)
3. [ServiceCollectionExtensions Class](#servicecollectionextensions-class)
4. [Metrics Emitted](#metrics-emitted)
5. [Exception Reference](#exception-reference)

## MeteredMemoryCache Class

A decorator implementation of `IMemoryCache` that emits OpenTelemetry metrics for cache operations.

### Namespace

```csharp
CacheImplementations
```

### Inheritance

```csharp
public sealed class MeteredMemoryCache : IMemoryCache, IDisposable
```

### Properties

#### Name

```csharp
public string? Name { get; }
```

Gets the logical name of this cache instance, if provided during construction.

**Returns:** The cache name or `null` if no name was specified.

### Constructors

#### MeteredMemoryCache(IMemoryCache, Meter, string?, bool)

```csharp
public MeteredMemoryCache(
    IMemoryCache innerCache,
    Meter meter,
    string? cacheName = null,
    bool disposeInner = false)
```

Creates a new MeteredMemoryCache instance with basic configuration.

**Parameters:**

- `innerCache` - The underlying IMemoryCache implementation to decorate
- `meter` - The OpenTelemetry Meter instance for creating counters
- `cacheName` - Optional logical name for the cache (used in metrics tags)
- `disposeInner` - Whether to dispose the inner cache when this instance is disposed

**Exceptions:**

- `ArgumentNullException` - Thrown when `innerCache` or `meter` is null

**Example:**

```csharp
var innerCache = new MemoryCache(new MemoryCacheOptions());
var meter = new Meter("MyApp.Cache");
var meteredCache = new MeteredMemoryCache(innerCache, meter, "user-cache", true);
```

#### MeteredMemoryCache(IMemoryCache, Meter, MeteredMemoryCacheOptions)

```csharp
public MeteredMemoryCache(
    IMemoryCache innerCache,
    Meter meter,
    MeteredMemoryCacheOptions options)
```

Creates a new MeteredMemoryCache instance with advanced configuration options.

**Parameters:**

- `innerCache` - The underlying IMemoryCache implementation to decorate
- `meter` - The OpenTelemetry Meter instance for creating counters
- `options` - Configuration options for the metered cache

**Exceptions:**

- `ArgumentNullException` - Thrown when any parameter is null

**Example:**

```csharp
var options = new MeteredMemoryCacheOptions
{
    CacheName = "user-cache",
    DisposeInner = true,
    AdditionalTags = { ["environment"] = "production", ["region"] = "us-west-2" }
};
var meteredCache = new MeteredMemoryCache(innerCache, meter, options);
```

### Usage with Extension Methods

`MeteredMemoryCache` implements the `IMemoryCache` interface and works seamlessly with all extension methods from `Microsoft.Extensions.Caching.Memory.CacheExtensions`. Metrics are automatically tracked for all operations.

**Recommended Usage Pattern:**

```csharp
using Microsoft.Extensions.Caching.Memory;

// Set values with type safety
cache.Set("user:123", userData, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
});

// Get values with type safety
if (cache.TryGetValue<UserData>("user:123", out var user))
{
    Console.WriteLine($"Found user: {user.Name}");
}

// Get or create pattern
var result = cache.GetOrCreate($"product:{productId}", entry =>
{
    entry.SlidingExpiration = TimeSpan.FromMinutes(10);
    return productService.GetProduct(productId);
});

// Async version
var asyncResult = await cache.GetOrCreateAsync($"order:{orderId}", async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
    return await orderService.GetOrderAsync(orderId);
});
```

**All metrics are automatically emitted** through the underlying `CreateEntry` and `TryGetValue` interface methods that the extension methods use internally.

### Methods

> **Note:** `MeteredMemoryCache` implements only the `IMemoryCache` interface methods. For strongly-typed operations like `Set<T>`, `TryGetValue<T>`, and `GetOrCreate<T>`, use the extension methods from `Microsoft.Extensions.Caching.Memory.CacheExtensions`. These extension methods work seamlessly with `MeteredMemoryCache` and metrics are automatically tracked.

#### TryGetValue(object, out object?)

```csharp
public bool TryGetValue(object key, out object? value)
```

Implementation of `IMemoryCache.TryGetValue` that records hit/miss metrics.

**Parameters:**

- `key` - The cache key to look up
- `value` - When this method returns, contains the cached value if found

**Returns:** `true` if the key was found; otherwise, `false`

**Exceptions:**

- `ArgumentNullException` - Thrown when `key` is null
- `ObjectDisposedException` - Thrown when the cache has been disposed

**Metrics Emitted:**

- `cache_hits_total` (if value found)
- `cache_misses_total` (if value not found)

#### CreateEntry(object)

```csharp
public ICacheEntry CreateEntry(object key)
```

Creates a cache entry and registers an eviction callback to record eviction metrics.

**Parameters:**

- `key` - The cache key

**Returns:** A new `ICacheEntry` instance

**Exceptions:**

- `ArgumentNullException` - Thrown when `key` is null
- `ObjectDisposedException` - Thrown when the cache has been disposed

**Metrics Emitted:**

- `cache_evictions_total` (when the entry is later evicted)

**Example:**

```csharp
using var entry = cache.CreateEntry("user:123");
entry.Value = userData;
entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
```

#### Remove(object)

```csharp
public void Remove(object key)
```

Removes an item from the cache. If the item exists, its eviction callback will record an eviction metric.

**Parameters:**

- `key` - The cache key to remove

**Exceptions:**

- `ArgumentNullException` - Thrown when `key` is null
- `ObjectDisposedException` - Thrown when the cache has been disposed

**Metrics Emitted:**

- `cache_evictions_total` (if the key existed and had an eviction callback)

#### Dispose()

```csharp
public void Dispose()
```

Disposes the MeteredMemoryCache and optionally the inner cache (based on constructor configuration).

## MeteredMemoryCacheOptions Class

Configuration options for MeteredMemoryCache behavior and metrics.

### Namespace

```csharp
CacheImplementations
```

### Properties

#### CacheName

```csharp
public string? CacheName { get; set; }
```

Gets or sets the logical name for the cache instance. This value is used as the `cache.name` tag in emitted metrics.

**Default:** `null`

#### DisposeInner

```csharp
public bool DisposeInner { get; set; }
```

Gets or sets whether MeteredMemoryCache should dispose the inner IMemoryCache when it is disposed.

**Default:** `false`

#### AdditionalTags

```csharp
[Required]
public IDictionary<string, object?> AdditionalTags { get; set; }
```

Gets or sets additional tags to include in all emitted metrics. The dictionary uses ordinal string comparison for keys.

**Default:** Empty dictionary with `StringComparer.Ordinal`

**Validation:** Cannot be set to null

**Example:**

```csharp
var options = new MeteredMemoryCacheOptions
{
    CacheName = "user-cache",
    AdditionalTags =
    {
        ["environment"] = "production",
        ["region"] = "us-west-2",
        ["version"] = "1.0.0"
    }
};
```

## ServiceCollectionExtensions Class

Extension methods for registering MeteredMemoryCache with dependency injection.

### Namespace

```csharp
CacheImplementations
```

### Methods

#### AddNamedMeteredMemoryCache

```csharp
public static IServiceCollection AddNamedMeteredMemoryCache(
    this IServiceCollection services,
    string cacheName,
    Action<MeteredMemoryCacheOptions>? configureOptions = null,
    string? meterName = null)
```

Registers a named MeteredMemoryCache with metrics in the service collection.

**Parameters:**

- `services` - The service collection
- `cacheName` - The logical cache name (for metrics tag)
- `configureOptions` - Optional configuration delegate for MeteredMemoryCacheOptions
- `meterName` - Optional meter name (defaults to "MeteredMemoryCache")

**Returns:** The service collection for method chaining

**Exceptions:**

- `ArgumentNullException` - Thrown when `services` is null
- `ArgumentException` - Thrown when `cacheName` is null, empty, or whitespace

**Registration Details:**

- Registers the cache as both a keyed service (`IMemoryCache` with key `cacheName`) and as a singleton
- Registers a `Meter` instance if not already registered
- Configures options validation with `IValidateOptions<MeteredMemoryCacheOptions>`
- The inner cache is automatically disposed when the service provider is disposed

**Example:**

```csharp
services.AddNamedMeteredMemoryCache("user-cache", options =>
{
    options.AdditionalTags["environment"] = "production";
}, "MyApp.Cache");
```

#### DecorateMemoryCacheWithMetrics

```csharp
public static IServiceCollection DecorateMemoryCacheWithMetrics(
    this IServiceCollection services,
    string? cacheName = null,
    string? meterName = null,
    Action<MeteredMemoryCacheOptions>? configureOptions = null)
```

Decorates an existing IMemoryCache registration with MeteredMemoryCache for metrics.

**Parameters:**

- `services` - The service collection
- `cacheName` - Optional cache name for metrics tag
- `meterName` - Optional meter name (defaults to "MeteredMemoryCache")
- `configureOptions` - Optional configuration delegate for MeteredMemoryCacheOptions

**Returns:** The service collection for method chaining

**Exceptions:**

- `ArgumentNullException` - Thrown when `services` is null
- `InvalidOperationException` - Thrown when no existing IMemoryCache registration is found

**Registration Details:**

- Requires an existing `IMemoryCache` registration
- Replaces the existing registration with a decorated version
- Preserves the original service lifetime
- Registers a `Meter` instance if not already registered
- Configures options validation

**Example:**

```csharp
// First register a memory cache
services.AddMemoryCache();

// Then decorate it with metrics
services.DecorateMemoryCacheWithMetrics("main-cache", "MyApp.Cache", options =>
{
    options.AdditionalTags["component"] = "web-api";
});
```

## Metrics Emitted

MeteredMemoryCache emits three types of metrics following OpenTelemetry conventions:

### cache_hits_total

- **Type:** Counter<long>
- **Description:** Total number of cache hits
- **Emitted by:** `TryGetValue` (and extension methods that call it: `TryGetValue<T>`, `Get<T>`, `GetOrCreate<T>`, etc.)
- **Tags:** `cache.name` (if specified), additional tags from options

### cache_misses_total

- **Type:** Counter<long>
- **Description:** Total number of cache misses
- **Emitted by:** `TryGetValue` (and extension methods that call it: `TryGetValue<T>`, `Get<T>`, `GetOrCreate<T>`, etc.)
- **Tags:** `cache.name` (if specified), additional tags from options

### cache_evictions_total

- **Type:** Counter<long>
- **Description:** Total number of cache evictions
- **Emitted by:** Eviction callbacks registered by `CreateEntry` (called by extension methods like `Set<T>`, `GetOrCreate<T>`, etc.)
- **Tags:**
  - `cache.name` (if specified)
  - `reason` - The eviction reason (Removed, Replaced, Expired, TokenExpired, Capacity)
  - Additional tags from options

### Metric Tags

All metrics include dimensional tags for filtering and aggregation:

- **cache.name**: The logical name of the cache (if specified)
- **Additional tags**: Custom tags specified in `MeteredMemoryCacheOptions.AdditionalTags`
- **reason** (evictions only): The reason for eviction as a string representation of `EvictionReason` enum

## Exception Reference

### ArgumentNullException

Thrown when required parameters are null:

- Constructor parameters (`innerCache`, `meter`, `options`)
- Method parameters (`key`, `factory`)

### ArgumentException

Thrown when parameters have invalid values:

- `cacheName` in `AddNamedMeteredMemoryCache` is null, empty, or whitespace

### ObjectDisposedException

Thrown when attempting to use a disposed MeteredMemoryCache instance.

### InvalidOperationException

Thrown in the following scenarios:

- `DecorateMemoryCacheWithMetrics` called without existing IMemoryCache registration
- Unable to resolve inner IMemoryCache instance during decoration

> **Note:** The `GetOrCreate<T>` extension method from `CacheExtensions` allows null return values. If you need null validation, implement it in your factory function.

## Performance Considerations

- **Read Operations**: Add approximately 15-40ns overhead for hit/miss metric recording
- **Write Operations**: Add approximately 1-14% overhead for eviction callback registration
- **Memory Usage**: +200 bytes per MeteredMemoryCache instance, +160 bytes per cached item with eviction callback
- **Thread Safety**: All operations are thread-safe; metric emission uses lock-free counters
- **Scalability**: Linear scaling with operation rate; no global locks or contention points

## Best Practices

1. **Cache Naming**: Use descriptive, hierarchical names (e.g., "user.profile", "product.catalog")
2. **Tag Usage**: Keep additional tags minimal and avoid high-cardinality values
3. **Disposal**: Use dependency injection for automatic disposal management
4. **Monitoring**: Set up alerts for cache hit rate thresholds and eviction rate spikes
5. **Testing**: Use the metric collection harness from unit tests for validation

## See Also

- [Usage Guide](MeteredMemoryCache.md)
- [OpenTelemetry Integration](OpenTelemetryIntegration.md)
- [Multi-Cache Scenarios](MultiCacheScenarios.md)
- [Performance Characteristics](PerformanceCharacteristics.md)
- [Troubleshooting Guide](Troubleshooting.md)
