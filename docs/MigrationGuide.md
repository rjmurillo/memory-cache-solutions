# MeteredMemoryCache Migration Guide

## Overview

This guide provides step-by-step instructions for migrating from various existing implementations to MeteredMemoryCache. Whether you're using raw `IMemoryCache`, custom metrics solutions, other caching libraries, or manual logging, this guide will help you transition smoothly while maintaining or improving your observability.

## Table of Contents

- [Migration Scenarios](#migration-scenarios)
- [Pre-Migration Assessment](#pre-migration-assessment)
- [Migration from Raw IMemoryCache](#migration-from-raw-imemorycache)
- [Migration from Custom Metrics Solutions](#migration-from-custom-metrics-solutions)
- [Migration from Other Caching Libraries](#migration-from-other-caching-libraries)
- [Migration from Manual Logging](#migration-from-manual-logging)
- [Advanced Migration Scenarios](#advanced-migration-scenarios)
- [Post-Migration Validation](#post-migration-validation)
- [Common Issues and Solutions](#common-issues-and-solutions)
- [Performance Impact Assessment](#performance-impact-assessment)

## Migration Scenarios

### Scenario 1: Basic IMemoryCache Usage

- Using `IMemoryCache` directly without any metrics
- Simple get/set operations
- No custom observability

### Scenario 2: Custom Metrics Implementation

- Existing custom counters or metrics
- Manual instrumentation around cache operations
- Custom dashboards or alerting

### Scenario 3: Third-Party Cache Libraries

- Using libraries like LazyCache, EasyCaching, or CacheManager
- Need to maintain existing API patterns
- Complex caching strategies

### Scenario 4: Manual Logging Approach

- Using ILogger for cache operation tracking
- Log-based metrics collection
- Custom log parsing for observability

## Pre-Migration Assessment

Before starting migration, assess your current implementation:

### 1. Inventory Current Cache Usage

```csharp
// Document all cache instances and their purposes
var cacheInventory = new[]
{
    new { Name = "UserCache", Purpose = "User profile data", Size = "~1000 entries" },
    new { Name = "ProductCache", Purpose = "Product catalog", Size = "~5000 entries" },
    new { Name = "SessionCache", Purpose = "User sessions", Size = "~10000 entries" }
};
```

### 2. Identify Current Metrics

```csharp
// List existing metrics being collected
var currentMetrics = new[]
{
    "cache_hits_total",
    "cache_misses_total",
    "cache_size_bytes",
    "cache_evictions_total",
    "cache_operation_duration_ms"
};
```

### 3. Document Dependencies

- OpenTelemetry configuration
- Metrics exporters (Prometheus, OTLP, etc.)
- Dashboard configurations
- Alerting rules

### 4. Performance Baseline

Before migration, capture baseline performance metrics:

```csharp
// Example baseline measurement
public class CachePerformanceBaseline
{
    public TimeSpan AverageGetLatency { get; set; }
    public TimeSpan AverageSetLatency { get; set; }
    public long OperationsPerSecond { get; set; }
    public double HitRatio { get; set; }
    public long MemoryUsage { get; set; }
}
```

## Migration from Raw IMemoryCache

### Before: Raw IMemoryCache

```csharp
public class UserService
{
    private readonly IMemoryCache _cache;

    public UserService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<User> GetUserAsync(int userId)
    {
        var cacheKey = $"user:{userId}";

        if (_cache.TryGetValue(cacheKey, out User cachedUser))
        {
            return cachedUser;
        }

        var user = await LoadUserFromDatabaseAsync(userId);

        _cache.Set(cacheKey, user, TimeSpan.FromMinutes(5));

        return user;
    }
}

// Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    services.AddMemoryCache();
    services.AddTransient<UserService>();
}
```

### After: MeteredMemoryCache

```csharp
public class UserService
{
    private readonly IMemoryCache _cache;

    public UserService(IMemoryCache cache)
    {
        _cache = cache; // Now automatically instrumented!
    }

    public async Task<User> GetUserAsync(int userId)
    {
        var cacheKey = $"user:{userId}";

        // Same code - metrics are collected automatically
        if (_cache.TryGetValue(cacheKey, out User cachedUser))
        {
            return cachedUser;
        }

        var user = await LoadUserFromDatabaseAsync(userId);

        _cache.Set(cacheKey, user, TimeSpan.FromMinutes(5));

        return user;
    }
}

// Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    // Add OpenTelemetry first
    services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddMeter("MyApp.Cache")
            .AddOtlpExporter());

    // Register MeteredMemoryCache instead of raw MemoryCache
    services.AddNamedMeteredMemoryCache("user-cache", meterName: "MyApp.Cache");

    services.AddTransient<UserService>();
}
```

### Migration Steps

1. **Add OpenTelemetry Configuration**

   ```csharp
   services.AddOpenTelemetry()
       .WithMetrics(metrics => metrics
           .AddMeter("YourApp.Cache")
           .AddPrometheusExporter() // or your preferred exporter
           .AddOtlpExporter());
   ```

2. **Replace Cache Registration**

   ```csharp
   // Before
   services.AddMemoryCache();

   // After
   services.AddNamedMeteredMemoryCache("main-cache",
       meterName: "YourApp.Cache",
       configure: options =>
       {
           options.SizeLimit = 1000;
           options.CompactionPercentage = 0.2;
       });
   ```

3. **Update Dependencies (if needed)**

   ```xml
   <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.6.0" />
   <PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.6.0-rc.1" />
   ```

4. **No Code Changes Required**
   - Existing cache usage code remains unchanged
   - Metrics are automatically collected

## Migration from Custom Metrics Solutions

### Before: Custom Metrics Implementation

```csharp
public class InstrumentedMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly IMetrics _metrics;
    private readonly Counter<long> _hitCounter;
    private readonly Counter<long> _missCounter;

    public InstrumentedMemoryCache(IMemoryCache inner, IMetrics metrics)
    {
        _inner = inner;
        _metrics = metrics;
        _hitCounter = _metrics.CreateCounter<long>("cache_hits_total");
        _missCounter = _metrics.CreateCounter<long>("cache_misses_total");
    }

    public bool TryGetValue(object key, out object value)
    {
        var result = _inner.TryGetValue(key, out value);

        if (result)
        {
            _hitCounter.Add(1);
        }
        else
        {
            _missCounter.Add(1);
        }

        return result;
    }

    // ... other IMemoryCache methods with similar instrumentation
}

// Registration
services.AddSingleton<IMemoryCache>(provider =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var metrics = provider.GetRequiredService<IMetrics>();
    return new InstrumentedMemoryCache(innerCache, metrics);
});
```

### After: MeteredMemoryCache

```csharp
// No custom implementation needed!

// Registration
services.AddNamedMeteredMemoryCache("main-cache",
    meterName: "YourApp.Cache");
```

### Migration Steps

1. **Remove Custom Implementation**

   - Delete custom wrapper classes
   - Remove manual counter creation
   - Clean up custom metric registration

2. **Update Metric Names (if needed)**

   ```csharp
   // If you need to maintain existing metric names for dashboards
   services.AddNamedMeteredMemoryCache("main-cache",
       meterName: "YourApp.Cache", // Use your existing meter name
       configure: options =>
       {
           // MeteredMemoryCache uses standard OpenTelemetry names:
           // - cache_hits_total
           // - cache_misses_total
           // - cache_evictions_total
       });
   ```

3. **Update Dashboard Queries**

   ```promql
   # Before (custom names)
   rate(my_cache_hits[5m])
   rate(my_cache_misses[5m])

   # After (standard names)
   rate(cache_hits_total{cache_name="main-cache"}[5m])
   rate(cache_misses_total{cache_name="main-cache"}[5m])
   ```

4. **Preserve Historical Data**
   ```promql
   # Union query to bridge historical and new data
   (
     rate(my_cache_hits[5m]) or
     rate(cache_hits_total{cache_name="main-cache"}[5m])
   )
   ```

## Migration from Other Caching Libraries

### LazyCache Migration

#### Before: LazyCache

```csharp
public class ProductService
{
    private readonly IAppCache _cache;

    public ProductService(IAppCache cache)
    {
        _cache = cache;
    }

    public async Task<Product> GetProductAsync(int productId)
    {
        return await _cache.GetOrAddAsync(
            $"product:{productId}",
            () => LoadProductFromDatabaseAsync(productId),
            TimeSpan.FromMinutes(10));
    }
}

// Registration
services.AddLazyCache();
```

#### After: MeteredMemoryCache with Extension Methods

```csharp
public class ProductService
{
    private readonly IMemoryCache _cache;

    public ProductService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<Product> GetProductAsync(int productId)
    {
        return await _cache.GetOrCreateAsync(
            $"product:{productId}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await LoadProductFromDatabaseAsync(productId);
            });
    }
}

// Registration
services.AddNamedMeteredMemoryCache("product-cache",
    meterName: "MyApp.Cache");
```

### EasyCaching Migration

#### Before: EasyCaching

```csharp
public class OrderService
{
    private readonly IEasyCachingProvider _cache;

    public OrderService(IEasyCachingProvider cache)
    {
        _cache = cache;
    }

    public async Task<Order> GetOrderAsync(int orderId)
    {
        var cacheKey = $"order:{orderId}";
        var cached = await _cache.GetAsync<Order>(cacheKey);

        if (cached.HasValue)
        {
            return cached.Value;
        }

        var order = await LoadOrderFromDatabaseAsync(orderId);
        await _cache.SetAsync(cacheKey, order, TimeSpan.FromMinutes(15));

        return order;
    }
}

// Registration
services.AddEasyCaching(options =>
{
    options.UseInMemory("default");
});
```

#### After: MeteredMemoryCache

```csharp
public class OrderService
{
    private readonly IMemoryCache _cache;

    public OrderService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<Order> GetOrderAsync(int orderId)
    {
        var cacheKey = $"order:{orderId}";

        if (_cache.TryGetValue(cacheKey, out Order cachedOrder))
        {
            return cachedOrder;
        }

        var order = await LoadOrderFromDatabaseAsync(orderId);

        _cache.Set(cacheKey, order, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        });

        return order;
    }
}

// Registration
services.AddNamedMeteredMemoryCache("order-cache",
    meterName: "MyApp.Cache");
```

## Migration from Manual Logging

### Before: ILogger-Based Tracking

```csharp
public class CacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public T Get<T>(string key)
    {
        using var activity = Activity.StartActivity("Cache.Get");
        activity?.SetTag("cache.key", key);

        var stopwatch = Stopwatch.StartNew();

        if (_cache.TryGetValue(key, out T value))
        {
            stopwatch.Stop();
            _logger.LogInformation("Cache hit for key {CacheKey} in {Duration}ms",
                key, stopwatch.ElapsedMilliseconds);

            activity?.SetTag("cache.hit", true);
            return value;
        }

        stopwatch.Stop();
        _logger.LogInformation("Cache miss for key {CacheKey} in {Duration}ms",
            key, stopwatch.ElapsedMilliseconds);

        activity?.SetTag("cache.hit", false);
        return default(T);
    }

    public void Set<T>(string key, T value, TimeSpan expiration)
    {
        using var activity = Activity.StartActivity("Cache.Set");
        activity?.SetTag("cache.key", key);

        var stopwatch = Stopwatch.StartNew();

        _cache.Set(key, value, expiration);

        stopwatch.Stop();
        _logger.LogInformation("Cache set for key {CacheKey} in {Duration}ms",
            key, stopwatch.ElapsedMilliseconds);
    }
}
```

### After: MeteredMemoryCache

```csharp
public class CacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache = cache; // Metrics automatically collected
        _logger = logger;
    }

    public T Get<T>(string key)
    {
        // Simplified - no manual instrumentation needed
        if (_cache.TryGetValue(key, out T value))
        {
            return value;
        }

        return default(T);
    }

    public void Set<T>(string key, T value, TimeSpan expiration)
    {
        // Simplified - no manual instrumentation needed
        _cache.Set(key, value, expiration);
    }
}

// Registration includes structured logging configuration
services.AddNamedMeteredMemoryCache("main-cache",
    meterName: "MyApp.Cache");

// Configure logging to complement metrics
services.Configure<LoggerFilterOptions>(options =>
{
    // Reduce noise from automatic cache instrumentation if desired
    options.Rules.Add(new LoggerFilterRule(null, "MyApp.Cache", LogLevel.Warning, null));
});
```

### Migration Steps

1. **Remove Manual Instrumentation**

   - Delete custom timing code
   - Remove manual counter increments
   - Clean up activity/span creation

2. **Preserve Important Logging**

   ```csharp
   public class CacheService
   {
       private readonly IMemoryCache _cache;
       private readonly ILogger<CacheService> _logger;

       public T Get<T>(string key)
       {
           if (_cache.TryGetValue(key, out T value))
           {
               // Keep business-relevant logging
               _logger.LogDebug("Retrieved {ItemType} from cache for key {CacheKey}",
                   typeof(T).Name, key);
               return value;
           }

           return default(T);
       }
   }
   ```

3. **Update Log Processing**
   - Remove log parsing for metrics
   - Focus logs on business events
   - Use structured logging for debugging

## Advanced Migration Scenarios

### Multi-Tenant Caching

#### Before: Custom Multi-Tenant Implementation

```csharp
public class MultiTenantCacheService
{
    private readonly Dictionary<string, IMemoryCache> _tenantCaches;
    private readonly ILogger _logger;

    public MultiTenantCacheService(ILogger<MultiTenantCacheService> logger)
    {
        _tenantCaches = new Dictionary<string, IMemoryCache>();
        _logger = logger;
    }

    public IMemoryCache GetCacheForTenant(string tenantId)
    {
        if (!_tenantCaches.TryGetValue(tenantId, out var cache))
        {
            cache = new MemoryCache(new MemoryCacheOptions());
            _tenantCaches[tenantId] = cache;
            _logger.LogInformation("Created cache for tenant {TenantId}", tenantId);
        }

        return cache;
    }
}
```

#### After: MeteredMemoryCache Multi-Tenant

```csharp
public class MultiTenantCacheService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IMemoryCache> _tenantCaches;

    public MultiTenantCacheService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _tenantCaches = new ConcurrentDictionary<string, IMemoryCache>();
    }

    public IMemoryCache GetCacheForTenant(string tenantId)
    {
        return _tenantCaches.GetOrAdd(tenantId, tid =>
        {
            // Create metered cache with tenant-specific name
            var meter = new Meter("MyApp.Cache");
            var innerCache = new MemoryCache(new MemoryCacheOptions());

            return new MeteredMemoryCache(innerCache, meter, $"tenant-{tid}");
        });
    }
}

// Registration
services.AddSingleton<MultiTenantCacheService>();
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter());
```

### Cache Hierarchies

#### Before: L1/L2 Cache Implementation

```csharp
public class HierarchicalCacheService
{
    private readonly IMemoryCache _l1Cache;
    private readonly IDistributedCache _l2Cache;
    private readonly ILogger _logger;

    public async Task<T> GetAsync<T>(string key)
    {
        // Try L1 first
        if (_l1Cache.TryGetValue(key, out T value))
        {
            _logger.LogDebug("L1 cache hit for {Key}", key);
            return value;
        }

        // Try L2
        var serialized = await _l2Cache.GetStringAsync(key);
        if (serialized != null)
        {
            value = JsonSerializer.Deserialize<T>(serialized);
            _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
            _logger.LogDebug("L2 cache hit for {Key}", key);
            return value;
        }

        _logger.LogDebug("Cache miss for {Key}", key);
        return default(T);
    }
}
```

#### After: MeteredMemoryCache Hierarchies

```csharp
public class HierarchicalCacheService
{
    private readonly IMemoryCache _l1Cache;
    private readonly IDistributedCache _l2Cache;

    public HierarchicalCacheService(
        [FromKeyedServices("l1-cache")] IMemoryCache l1Cache,
        IDistributedCache l2Cache)
    {
        _l1Cache = l1Cache; // Automatically metered with "l1-cache" name
        _l2Cache = l2Cache;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        // L1 metrics automatically collected
        if (_l1Cache.TryGetValue(key, out T value))
        {
            return value;
        }

        // L2 cache logic (can also be instrumented separately)
        var serialized = await _l2Cache.GetStringAsync(key);
        if (serialized != null)
        {
            value = JsonSerializer.Deserialize<T>(serialized);
            _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
            return value;
        }

        return default(T);
    }
}

// Registration
services.AddKeyedService<IMemoryCache>("l1-cache", (provider, key) =>
{
    var meter = provider.GetRequiredService<Meter>();
    var innerCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 1000
    });
    return new MeteredMemoryCache(innerCache, meter, "l1-cache");
});

services.AddKeyedService<IMemoryCache>("l2-promotion", (provider, key) =>
{
    var meter = provider.GetRequiredService<Meter>();
    var innerCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 100
    });
    return new MeteredMemoryCache(innerCache, meter, "l2-promotion");
});
```

## Post-Migration Validation

### 1. Functional Validation

```csharp
[Test]
public async Task MigrationValidation_CacheBehaviorUnchanged()
{
    // Arrange
    var serviceProvider = BuildServiceProvider();
    var cache = serviceProvider.GetRequiredService<IMemoryCache>();
    var testKey = "test-key";
    var testValue = "test-value";

    // Act & Assert
    // Cache should be empty initially
    Assert.False(cache.TryGetValue(testKey, out _));

    // Set should work
    cache.Set(testKey, testValue, TimeSpan.FromMinutes(5));

    // Get should work
    Assert.True(cache.TryGetValue(testKey, out var retrieved));
    Assert.Equal(testValue, retrieved);

    // Expiration should work
    cache.Set(testKey, testValue, TimeSpan.FromMilliseconds(100));
    await Task.Delay(200);
    Assert.False(cache.TryGetValue(testKey, out _));
}
```

### 2. Metrics Validation

```csharp
[Test]
public async Task MigrationValidation_MetricsAreEmitted()
{
    // Arrange
    using var meterProvider = Sdk.CreateMeterProviderBuilder()
        .AddMeter("Test.Cache")
        .AddInMemoryExporter(out var exportedItems)
        .Build();

    var serviceProvider = BuildServiceProvider("Test.Cache");
    var cache = serviceProvider.GetRequiredService<IMemoryCache>();

    // Act
    cache.TryGetValue("missing-key", out _); // Miss
    cache.Set("test-key", "test-value");
    cache.TryGetValue("test-key", out _); // Hit

    // Force metrics collection
    meterProvider?.ForceFlush(TimeSpan.FromSeconds(5));

    // Assert
    var metrics = exportedItems.ToArray();
    Assert.Contains(metrics, m => m.Name == "cache_hits_total");
    Assert.Contains(metrics, m => m.Name == "cache_misses_total");
}
```

### 3. Performance Validation

```csharp
[Benchmark]
[Arguments(1000)]
public void MigrationValidation_PerformanceRegression(int operations)
{
    var cache = _serviceProvider.GetRequiredService<IMemoryCache>();

    for (int i = 0; i < operations; i++)
    {
        var key = $"key-{i % 100}"; // 1% hit rate

        if (!cache.TryGetValue(key, out _))
        {
            cache.Set(key, $"value-{i}", TimeSpan.FromMinutes(5));
        }
    }
}
```

### 4. Integration Validation

```csharp
[Test]
public async Task MigrationValidation_OpenTelemetryIntegration()
{
    // Arrange
    using var tracerProvider = Sdk.CreateTracerProviderBuilder()
        .AddSource("Test.Application")
        .AddInMemoryExporter(out var exportedActivities)
        .Build();

    using var meterProvider = Sdk.CreateMeterProviderBuilder()
        .AddMeter("Test.Cache")
        .AddInMemoryExporter(out var exportedMetrics)
        .Build();

    var serviceProvider = BuildServiceProvider("Test.Cache");
    var cache = serviceProvider.GetRequiredService<IMemoryCache>();

    // Act
    using var activity = Activity.StartActivity("Test Operation");
    cache.Set("test", "value");
    cache.TryGetValue("test", out _);

    tracerProvider?.ForceFlush(TimeSpan.FromSeconds(5));
    meterProvider?.ForceFlush(TimeSpan.FromSeconds(5));

    // Assert
    Assert.NotEmpty(exportedActivities);
    Assert.NotEmpty(exportedMetrics);
}
```

## Common Issues and Solutions

### Issue 1: Metric Name Conflicts

**Problem**: Existing dashboards use different metric names.

**Solution**:

```csharp
// Use custom meter name to avoid conflicts
services.AddNamedMeteredMemoryCache("legacy-cache",
    meterName: "Legacy.Cache.Metrics");

// Create bridge metrics if needed
services.AddSingleton<IHostedService, MetricsBridgeService>();

public class MetricsBridgeService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Periodically read new metrics and emit in old format
        // This is a temporary solution during migration
    }
}
```

### Issue 2: Performance Regression

**Problem**: MeteredMemoryCache adds overhead.

**Solution**:

```csharp
// Measure the overhead
[Benchmark]
public void RawCache() => _rawCache.TryGetValue("key", out _);

[Benchmark]
public void MeteredCache() => _meteredCache.TryGetValue("key", out _);

// Expected overhead: ~100ns per operation
// If higher, check for:
// 1. Excessive tag allocation
// 2. Frequent meter lookups
// 3. Inefficient metric emission
```

### Issue 3: Memory Leaks

**Problem**: Meter instances not disposed properly.

**Solution**:

```csharp
// Ensure proper disposal
services.AddSingleton<Meter>(provider => new Meter("MyApp.Cache"));

// Or use factory pattern
services.AddSingleton<IMeterFactory, MeterFactory>();
services.AddSingleton(provider =>
    provider.GetRequiredService<IMeterFactory>().Create("MyApp.Cache"));

// Dispose in application shutdown
public class Startup
{
    public void Configure(IApplicationBuilder app, IHostApplicationLifetime lifetime)
    {
        lifetime.ApplicationStopping.Register(() =>
        {
            var meter = app.ApplicationServices.GetService<Meter>();
            meter?.Dispose();
        });
    }
}
```

### Issue 4: Tag Cardinality Explosion

**Problem**: Too many unique cache names or tag values.

**Solution**:

```csharp
// Limit tag cardinality
services.AddNamedMeteredMemoryCache("user-cache",
    configure: options =>
    {
        // Avoid dynamic cache names like "user-cache-{userId}"
        // Use consistent, bounded names
        options.CacheName = "user-cache"; // Good
        // options.CacheName = $"user-cache-{userId}"; // Bad - unbounded
    });

// Use additional tags sparingly
var options = new MeteredMemoryCacheOptions
{
    CacheName = "main-cache",
    AdditionalTags = new Dictionary<string, object>
    {
        ["environment"] = "production", // Good - bounded values
        ["region"] = "us-west-2"       // Good - bounded values
        // ["user_id"] = userId        // Bad - unbounded values
    }
};
```

### Issue 5: Missing Metrics

**Problem**: Metrics not appearing in monitoring systems.

**Solution**:

```csharp
// Verify meter registration
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache") // Must match meter name
        .AddConsoleExporter()   // Add console exporter for debugging
        .AddOtlpExporter());

// Check metric emission
var meter = new Meter("MyApp.Cache");
var counter = meter.CreateCounter<long>("test_counter");
counter.Add(1); // Should appear in console output
```

## Performance Impact Assessment

### Expected Overhead

| Operation | Raw Cache | MeteredCache | Overhead | Percentage |
| --------- | --------- | ------------ | -------- | ---------- |
| Hit       | ~50ns     | ~150ns       | +100ns   | +200%      |
| Miss      | ~100ns    | ~200ns       | +100ns   | +100%      |
| Set       | ~200ns    | ~350ns       | +150ns   | +75%       |

### Memory Impact

- **Per Instance**: ~200 bytes (3 counters + tags)
- **Per Operation**: 0 additional allocations on hot path
- **Tag Storage**: 64 bytes per unique cache name

### Mitigation Strategies

1. **Batch Operations**: Use `GetOrCreate` patterns to reduce operation count
2. **Cache Warming**: Pre-populate caches to improve hit ratios
3. **Size Tuning**: Optimize cache sizes based on metrics
4. **Selective Instrumentation**: Only instrument critical caches

### Benchmark Template

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class MigrationBenchmark
{
    private IMemoryCache _rawCache;
    private IMemoryCache _meteredCache;

    [GlobalSetup]
    public void Setup()
    {
        _rawCache = new MemoryCache(new MemoryCacheOptions());

        var meter = new Meter("Benchmark.Cache");
        _meteredCache = new MeteredMemoryCache(_rawCache, meter, "test-cache");

        // Pre-populate for hit scenarios
        for (int i = 0; i < 100; i++)
        {
            _rawCache.Set($"key-{i}", $"value-{i}");
            _meteredCache.Set($"key-{i}", $"value-{i}");
        }
    }

    [Benchmark]
    public object RawCache_Hit() => _rawCache.TryGetValue("key-50", out var value) ? value : null;

    [Benchmark]
    public object MeteredCache_Hit() => _meteredCache.TryGetValue("key-50", out var value) ? value : null;

    [Benchmark]
    public void RawCache_Miss() => _rawCache.TryGetValue("missing-key", out _);

    [Benchmark]
    public void MeteredCache_Miss() => _meteredCache.TryGetValue("missing-key", out _);
}
```

## Migration Checklist

### Pre-Migration

- [ ] Document current cache usage patterns
- [ ] Identify existing metrics and dashboards
- [ ] Establish performance baseline
- [ ] Plan OpenTelemetry infrastructure
- [ ] Review cache naming strategy

### During Migration

- [ ] Update package references
- [ ] Configure OpenTelemetry metrics
- [ ] Replace cache registrations
- [ ] Update metric names in dashboards
- [ ] Test functionality in staging

### Post-Migration

- [ ] Validate metrics emission
- [ ] Compare performance benchmarks
- [ ] Update monitoring alerts
- [ ] Remove old instrumentation code
- [ ] Document new metrics for team

### Rollback Plan

- [ ] Keep old implementation available
- [ ] Feature flag for switching
- [ ] Monitoring for regression detection
- [ ] Quick revert procedure documented

## Additional Resources

- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [ASP.NET Core Metrics](https://docs.microsoft.com/en-us/aspnet/core/log-mon/metrics/)
- [Prometheus Naming Conventions](https://prometheus.io/docs/practices/naming/)
- [Grafana Dashboard Examples](https://grafana.com/grafana/dashboards/)

For questions or issues during migration, consult the [Troubleshooting Guide](./Troubleshooting.md) or create an issue in the repository.
