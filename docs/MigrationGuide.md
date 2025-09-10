# Migration Guide

This guide provides step-by-step instructions for migrating to MeteredMemoryCache from various existing caching implementations and custom metrics solutions.

## Table of Contents

- [Migration from Raw IMemoryCache](#migration-from-raw-imemorycache)
- [Migration from Custom Metrics Solutions](#migration-from-custom-metrics-solutions)
- [Migration from Other Caching Libraries](#migration-from-other-caching-libraries)
- [Migration from Manual Logging](#migration-from-manual-logging)
- [Breaking Changes and Compatibility](#breaking-changes-and-compatibility)
- [Performance Impact Assessment](#performance-impact-assessment)
- [Validation and Testing](#validation-and-testing)

## Migration from Raw IMemoryCache

### Before: Standard MemoryCache

```csharp
// Startup.cs or Program.cs
services.AddMemoryCache(options =>
{
    options.SizeLimit = 1000;
    options.CompactionPercentage = 0.25;
});

// Usage in service
public class ProductService
{
    private readonly IMemoryCache _cache;
    
    public ProductService(IMemoryCache cache)
    {
        _cache = cache;
    }
    
    public async Task<Product> GetProductAsync(int id)
    {
        if (_cache.TryGetValue($"product:{id}", out Product cached))
        {
            return cached;
        }
        
        var product = await _repository.GetProductAsync(id);
        _cache.Set($"product:{id}", product, TimeSpan.FromMinutes(15));
        return product;
    }
}
```

### After: MeteredMemoryCache with Metrics

```csharp
// Startup.cs or Program.cs
services.AddSingleton<Meter>(sp => new Meter("MyApp.Cache"));
services.AddSingleton<IMemoryCache>(sp =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 1000,
        CompactionPercentage = 0.25
    });
    var meter = sp.GetRequiredService<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "product-cache");
});

// Add OpenTelemetry
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter());

// Usage remains exactly the same - no code changes required!
public class ProductService
{
    private readonly IMemoryCache _cache;
    
    public ProductService(IMemoryCache cache)
    {
        _cache = cache; // Now automatically emits metrics
    }
    
    public async Task<Product> GetProductAsync(int id)
    {
        // Identical code - metrics are automatically emitted
        if (_cache.TryGetValue($"product:{id}", out Product cached))
        {
            return cached; // Hit metric emitted
        }
        
        var product = await _repository.GetProductAsync(id); // Miss metric emitted
        _cache.Set($"product:{id}", product, TimeSpan.FromMinutes(15)); // Eviction callback registered
        return product;
    }
}
```

### Migration Steps

1. **Install Required Packages**:
   ```xml
   <PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.0" />
   <PackageReference Include="OpenTelemetry" Version="1.6.0" />
   <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.6.0" />
   ```

2. **Update Service Registration**:
   - Wrap existing `IMemoryCache` with `MeteredMemoryCache`
   - Add `Meter` registration
   - Configure OpenTelemetry metrics

3. **No Code Changes Required**: Your existing cache usage code remains unchanged

4. **Validate Metrics**: Verify metrics are being emitted to your monitoring system

## Migration from Custom Metrics Solutions

### Before: Manual Metrics Tracking

```csharp
public class MetricsTrackingCache
{
    private readonly IMemoryCache _cache;
    private readonly IMetrics _metrics;
    
    public MetricsTrackingCache(IMemoryCache cache, IMetrics metrics)
    {
        _cache = cache;
        _metrics = metrics;
    }
    
    public bool TryGetValue<T>(object key, out T value)
    {
        var timer = _metrics.Measure.Timer.Time("cache.operation");
        var hit = _cache.TryGetValue(key, out var obj);
        
        if (hit && obj is T result)
        {
            _metrics.Measure.Counter.Increment("cache.hits", new MetricTags("cache", "user-cache"));
            value = result;
            timer.Dispose();
            return true;
        }
        
        _metrics.Measure.Counter.Increment("cache.misses", new MetricTags("cache", "user-cache"));
        value = default(T);
        timer.Dispose();
        return false;
    }
    
    public void Set<T>(object key, T value, MemoryCacheEntryOptions options = null)
    {
        _metrics.Measure.Counter.Increment("cache.sets", new MetricTags("cache", "user-cache"));
        _cache.Set(key, value, options);
    }
}
```

### After: MeteredMemoryCache

```csharp
// Service registration - much simpler
services.AddSingleton<Meter>(sp => new Meter("MyApp.Cache"));
services.AddNamedMeteredMemoryCache("user-cache", options =>
{
    options.SizeLimit = 1000;
});

// Usage - direct IMemoryCache interface
public class UserService
{
    private readonly IMemoryCache _cache;
    
    public UserService([FromKeyedServices("user-cache")] IMemoryCache cache)
    {
        _cache = cache; // Metrics automatically handled
    }
    
    public bool TryGetUser(int id, out User user)
    {
        // Automatic hit/miss tracking, no manual instrumentation
        return _cache.TryGetValue($"user:{id}", out user);
    }
    
    public void CacheUser(User user)
    {
        // Automatic eviction tracking, no manual instrumentation
        _cache.Set($"user:{user.Id}", user, TimeSpan.FromMinutes(30));
    }
}
```

### Migration Benefits

- **Reduced Code Complexity**: Remove 50-80% of manual metrics code
- **Standardized Metrics**: Consistent naming and tagging across all caches
- **Zero Allocation Overhead**: More efficient than manual timing
- **Automatic Eviction Tracking**: Previously difficult to implement correctly

### Migration Steps

1. **Identify Manual Metrics Code**: Find all custom cache instrumentation
2. **Replace with MeteredMemoryCache**: Use service extensions for registration
3. **Remove Manual Instrumentation**: Delete custom metrics tracking code
4. **Update Monitoring Dashboards**: Use standardized metric names
5. **Performance Test**: Verify improved performance with reduced overhead

## Migration from Other Caching Libraries

### From Microsoft.Extensions.Caching.Memory with IDistributedCache

```csharp
// Before: DistributedMemoryCache
services.AddDistributedMemoryCache();

// After: MeteredMemoryCache with equivalent functionality
services.AddSingleton<IMemoryCache>(sp =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var meter = sp.GetRequiredService<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "distributed-cache");
});
```

### From LazyCache

```csharp
// Before: LazyCache
services.AddLazyCache();

// After: MeteredMemoryCache with lazy loading pattern
services.AddSingleton<IMemoryCache>(sp =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var meter = sp.GetRequiredService<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "lazy-cache");
});

// Implement lazy loading with GetOrCreate
public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
{
    return await _cache.GetOrCreateAsync(key, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
        return await factory();
    });
}
```

### From StackExchange.Redis IDatabase

```csharp
// Before: Redis with manual serialization
public class RedisCache
{
    private readonly IDatabase _database;
    
    public async Task<T> GetAsync<T>(string key)
    {
        var value = await _database.StringGetAsync(key);
        return value.HasValue ? JsonSerializer.Deserialize<T>(value) : default(T);
    }
}

// After: MeteredMemoryCache for local caching
services.AddNamedMeteredMemoryCache("local-cache");

// Usage with automatic serialization handling
public class CacheService
{
    private readonly IMemoryCache _cache;
    
    public bool TryGet<T>(string key, out T value)
    {
        return _cache.TryGetValue(key, out value); // No serialization needed
    }
}
```

## Migration from Manual Logging

### Before: Custom Logging

```csharp
public class LoggingCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LoggingCache> _logger;
    
    public bool TryGetValue<T>(object key, out T value)
    {
        var stopwatch = Stopwatch.StartNew();
        var hit = _cache.TryGetValue(key, out var obj);
        stopwatch.Stop();
        
        if (hit && obj is T result)
        {
            _logger.LogInformation("Cache hit for key {Key} in {ElapsedMs}ms", key, stopwatch.ElapsedMilliseconds);
            value = result;
            return true;
        }
        
        _logger.LogInformation("Cache miss for key {Key} in {ElapsedMs}ms", key, stopwatch.ElapsedMilliseconds);
        value = default(T);
        return false;
    }
}
```

### After: MeteredMemoryCache with Structured Metrics

```csharp
// Automatic structured metrics instead of logs
services.AddNamedMeteredMemoryCache("logged-cache");

// Configure OpenTelemetry for structured observability
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddConsoleExporter() // For development
        .AddOtlpExporter());   // For production

// Usage - no manual logging required
public class Service
{
    private readonly IMemoryCache _cache;
    
    public bool TryGetValue<T>(object key, out T value)
    {
        // Automatic structured metrics collection
        return _cache.TryGetValue(key, out value);
    }
}
```

### Benefits of Structured Metrics vs Logging

| Aspect | Manual Logging | MeteredMemoryCache |
|--------|---------------|-------------------|
| **Performance** | High overhead (string formatting, I/O) | Low overhead (~40ns) |
| **Aggregation** | Requires log parsing | Native metric aggregation |
| **Alerting** | Complex log-based rules | Simple threshold-based |
| **Storage** | High volume, expensive | Compact, pre-aggregated |
| **Dashboards** | Requires log analytics | Native metric visualization |

## Breaking Changes and Compatibility

### API Compatibility

✅ **Fully Compatible**: MeteredMemoryCache implements `IMemoryCache` interface completely
✅ **No Code Changes**: Existing cache usage code works unchanged
✅ **Dependency Injection**: Seamless replacement in DI container

### Behavior Changes

⚠️ **Performance Impact**: 15-40ns overhead per operation (see [Performance Characteristics](PerformanceCharacteristics.md))
⚠️ **Memory Usage**: +200 bytes per MeteredMemoryCache instance
⚠️ **Metric Dependencies**: Requires OpenTelemetry setup for metric export

### Migration Risks

| Risk | Mitigation |
|------|------------|
| **Performance Regression** | Benchmark before/after, use BenchGate validation |
| **Memory Pressure** | Monitor memory usage, especially with many cache instances |
| **Metric Export Failures** | Configure fallback exporters, monitor export health |
| **Tag Cardinality** | Limit cache names and additional tags to prevent explosion |

## Performance Impact Assessment

### Before Migration Benchmarks

Run baseline benchmarks to establish current performance:

```csharp
[Benchmark]
public object CurrentCache_Hit()
{
    return _currentCache.TryGetValue("key", out var value) ? value : null;
}

[Benchmark]
public void CurrentCache_Set()
{
    _currentCache.Set("key", _testValue, TimeSpan.FromMinutes(1));
}
```

### After Migration Validation

Compare MeteredMemoryCache performance:

```csharp
[Benchmark]
public object MeteredCache_Hit()
{
    return _meteredCache.TryGetValue("key", out var value) ? value : null;
}

[Benchmark]
public void MeteredCache_Set()
{
    _meteredCache.Set("key", _testValue, TimeSpan.FromMinutes(1));
}
```

### Expected Performance Impact

Based on comprehensive benchmarks (see [Performance Characteristics](PerformanceCharacteristics.md)):

- **Cache Hits**: +15-40ns overhead (28-43% increase)
- **Cache Writes**: +180-400ns overhead (1-14% increase)
- **Memory**: +200 bytes per cache instance
- **No Contention**: Thread-safe with linear scaling

## Validation and Testing

### Pre-Migration Checklist

- [ ] Identify all cache usage patterns
- [ ] Document current performance characteristics
- [ ] Plan OpenTelemetry integration
- [ ] Design metric validation strategy
- [ ] Create rollback plan

### Migration Testing

1. **Unit Tests**: Verify functional compatibility
   ```csharp
   [Test]
   public void MeteredCache_BehavesLikeOriginal()
   {
       // Test all IMemoryCache operations match exactly
   }
   ```

2. **Integration Tests**: Validate metric emission
   ```csharp
   [Test]
   public void MeteredCache_EmitsExpectedMetrics()
   {
       // Verify hit/miss/eviction metrics
   }
   ```

3. **Performance Tests**: Benchmark overhead
   ```csharp
   [Test]
   public void MeteredCache_PerformanceAcceptable()
   {
       // Ensure <50ns overhead per operation
   }
   ```

### Post-Migration Validation

- [ ] Monitor metric emission in production
- [ ] Validate dashboard and alerting
- [ ] Performance monitoring for regressions
- [ ] Error rate monitoring
- [ ] Memory usage validation

### Rollback Strategy

If issues occur post-migration:

1. **Immediate**: Switch service registration back to raw IMemoryCache
2. **Metric Loss**: Acceptable during rollback period
3. **Data Preservation**: Cache data remains intact
4. **Quick Recovery**: <5 minute rollback time

## Common Migration Patterns

### Pattern 1: Gradual Migration

Migrate one cache at a time:

```csharp
// Week 1: User cache
services.AddNamedMeteredMemoryCache("user-cache");

// Week 2: Product cache  
services.AddNamedMeteredMemoryCache("product-cache");

// Week 3: Session cache
services.AddNamedMeteredMemoryCache("session-cache");
```

### Pattern 2: Feature Flag Migration

Use feature flags for safe migration:

```csharp
services.AddSingleton<IMemoryCache>(sp =>
{
    var featureFlags = sp.GetRequiredService<IFeatureFlags>();
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    
    if (featureFlags.IsEnabled("MeteredCache"))
    {
        var meter = sp.GetRequiredService<Meter>();
        return new MeteredMemoryCache(innerCache, meter, "main-cache");
    }
    
    return innerCache; // Fallback to raw cache
});
```

### Pattern 3: A/B Testing Migration

Compare performance between implementations:

```csharp
services.AddSingleton<IMemoryCache>("raw", sp => new MemoryCache(new MemoryCacheOptions()));
services.AddSingleton<IMemoryCache>("metered", sp =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var meter = sp.GetRequiredService<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "ab-test-cache");
});
```

## Related Documentation

- [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md) - Basic usage patterns
- [OpenTelemetry Integration](OpenTelemetryIntegration.md) - Metrics setup and configuration
- [Performance Characteristics](PerformanceCharacteristics.md) - Detailed benchmarks and analysis
- [Troubleshooting Guide](Troubleshooting.md) - Common migration issues and solutions
- [Best Practices](BestPractices.md) - Recommended patterns and configurations
