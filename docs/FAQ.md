# Frequently Asked Questions (FAQ)

This document answers common questions about MeteredMemoryCache, covering usage scenarios, performance considerations, troubleshooting, and integration patterns.

## Table of Contents

- [General Questions](#general-questions)
- [When to Use MeteredMemoryCache](#when-to-use-meteredmemorycache)
- [Performance and Overhead](#performance-and-overhead)
- [Configuration and Setup](#configuration-and-setup)
- [Monitoring and Metrics](#monitoring-and-metrics)
- [Troubleshooting](#troubleshooting)
- [Integration and Compatibility](#integration-and-compatibility)
- [Advanced Scenarios](#advanced-scenarios)

## General Questions

### Q: What is MeteredMemoryCache?

**A:** MeteredMemoryCache is a decorator that wraps any `IMemoryCache` implementation to automatically emit OpenTelemetry metrics for cache operations. It provides comprehensive observability for cache hits, misses, and evictions without requiring changes to your existing cache usage code.

### Q: Do I need to change my existing cache code?

**A:** No! MeteredMemoryCache implements the standard `IMemoryCache` interface, so your existing code works unchanged. Simply replace your cache registration in dependency injection, and metrics will be automatically emitted.

```csharp
// Before
services.AddMemoryCache();

// After - no code changes needed elsewhere!
services.AddSingleton<IMemoryCache>(sp =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var meter = sp.GetRequiredService<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "my-cache");
});
```

### Q: What metrics does MeteredMemoryCache emit?

**A:** MeteredMemoryCache emits three core counters:

- **`cache_hits_total`** - Number of successful cache retrievals
- **`cache_misses_total`** - Number of cache key lookups that failed
- **`cache_evictions_total`** - Number of items removed from cache (with reason tag)

All metrics include optional dimensional tags like `cache.name` for multi-cache scenarios.

### Q: Is MeteredMemoryCache thread-safe?

**A:** Yes, MeteredMemoryCache is fully thread-safe. It relies on the thread-safety of the underlying `IMemoryCache` implementation and uses thread-safe OpenTelemetry counters for metric emission.

## When to Use MeteredMemoryCache

### Q: When should I use MeteredMemoryCache vs raw IMemoryCache?

**A:** Use MeteredMemoryCache when you need:

✅ **Use MeteredMemoryCache when:**

- You want cache observability without manual instrumentation
- You need to monitor cache effectiveness and hit rates
- You're using multiple caches and need to track them separately
- You want to identify cache performance bottlenecks
- You're planning capacity or performance optimization
- You need alerts on cache health degradation

❌ **Consider raw IMemoryCache when:**

- Performance overhead is absolutely critical (< 40ns unacceptable)
- You're in a resource-constrained environment
- You already have comprehensive custom metrics
- You don't need cache observability

### Q: How do I decide between MeteredMemoryCache and other caching solutions?

**A:** Here's a decision matrix:

| Scenario                                 | Recommendation                           |
| ---------------------------------------- | ---------------------------------------- |
| **Local in-memory caching with metrics** | ✅ MeteredMemoryCache                    |
| **Distributed caching**                  | Use Redis/SQL with separate metrics      |
| **High-performance, no observability**   | Raw IMemoryCache                         |
| **Complex cache hierarchies**            | MeteredMemoryCache + custom coordination |
| **Temporary/experimental caching**       | Raw IMemoryCache                         |
| **Production systems**                   | ✅ MeteredMemoryCache                    |

### Q: Can I use MeteredMemoryCache with distributed caching?

**A:** MeteredMemoryCache is designed for local in-memory caching (`IMemoryCache`). For distributed caching:

- **Option 1**: Use `IDistributedCache` with separate instrumentation
- **Option 2**: Create a local cache layer with MeteredMemoryCache in front of distributed cache
- **Option 3**: Use MeteredMemoryCache for read-through caching with distributed cache as backing store

## Performance and Overhead

### Q: What is the performance impact of MeteredMemoryCache?

**A:** Based on comprehensive benchmarks:

| Operation        | Overhead                | Percentage Impact |
| ---------------- | ----------------------- | ----------------- |
| **Cache Hit**    | +15-40ns                | +28-43%           |
| **Cache Miss**   | +15-40ns                | +28-43%           |
| **Cache Write**  | +180-400ns              | +1-14%            |
| **Memory Usage** | +200 bytes per instance | Negligible        |

The overhead is generally acceptable for most applications. See [Performance Characteristics](PerformanceCharacteristics.md) for detailed analysis.

### Q: When is the overhead too high?

**A:** Consider the overhead problematic if:

- Your cache operations are in extremely hot paths (>1M ops/sec)
- You have very strict latency requirements (<100ns total)
- You're running in memory-constrained environments
- You're using hundreds of cache instances

**Mitigation strategies:**

- Use fewer, larger caches instead of many small ones
- Implement sampling (only meter a percentage of operations)
- Use raw IMemoryCache for ultra-high-performance scenarios

### Q: Does MeteredMemoryCache affect cache hit rates?

**A:** No, MeteredMemoryCache is a pure decorator and doesn't affect cache behavior, hit rates, or eviction patterns. It only observes and reports on operations performed by the underlying cache.

### Q: How can I minimize the performance impact?

**A:** Follow these optimization patterns:

```csharp
// 1. Use fewer cache instances with meaningful names
services.AddNamedMeteredMemoryCache("unified-cache");

// 2. Avoid high-cardinality tags
var options = new MeteredMemoryCacheOptions
{
    AdditionalTags = { ["region"] = "us-west" } // Low cardinality
    // Don't add user IDs or timestamps!
};

// 3. Use efficient key patterns
private static readonly string KeyPrefix = "user:profile:";
public string CreateKey(int userId) => KeyPrefix + userId.ToString();

// 4. Batch operations when possible
public async Task<Dictionary<int, User>> GetUsersAsync(int[] ids)
{
    // Single metric emission per batch instead of per item
}
```

## Configuration and Setup

### Q: How do I set up OpenTelemetry for MeteredMemoryCache?

**A:** Here's a complete setup:

```csharp
// 1. Register Meter
services.AddSingleton<Meter>(sp => new Meter("MyApp.Cache"));

// 2. Register MeteredMemoryCache
services.AddNamedMeteredMemoryCache("main-cache");

// 3. Configure OpenTelemetry
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter()     // For production
        .AddConsoleExporter()); // For development
```

### Q: How do I configure multiple caches with different settings?

**A:** Use the named cache registration pattern:

```csharp
// Fast, small cache for frequent data
services.AddNamedMeteredMemoryCache("hot-data", options =>
{
    options.SizeLimit = 10000;
    options.CompactionPercentage = 0.1;
});

// Large cache for expensive computations
services.AddNamedMeteredMemoryCache("expensive-results", options =>
{
    options.SizeLimit = 1000;
    options.CompactionPercentage = 0.25;
});

// Usage with keyed services
public class UserService
{
    public UserService([FromKeyedServices("hot-data")] IMemoryCache hotCache)
    {
        _hotCache = hotCache;
    }
}
```

### Q: Can I disable metrics for specific caches?

**A:** Yes, several approaches:

```csharp
// Option 1: Use raw IMemoryCache for specific scenarios
services.AddSingleton<IMemoryCache>("no-metrics", sp => new MemoryCache(new MemoryCacheOptions()));

// Option 2: Feature flag approach
services.AddSingleton<IMemoryCache>(sp =>
{
    var cache = new MemoryCache(new MemoryCacheOptions());
    var config = sp.GetRequiredService<IConfiguration>();

    if (config.GetValue<bool>("Cache:EnableMetrics"))
    {
        var meter = sp.GetRequiredService<Meter>();
        return new MeteredMemoryCache(cache, meter, "conditional-cache");
    }

    return cache;
});

// Option 3: Null meter (metrics get discarded)
var nullMeter = new Meter("null");
var cache = new MeteredMemoryCache(innerCache, nullMeter);
```

## Monitoring and Metrics

### Q: What should I monitor in production?

**A:** Essential metrics to track:

1. **Hit Rate**: `rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))`

   - Target: >80%, Alert: <70%

2. **Operations per Second**: `rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])`

   - Monitor for traffic patterns and capacity planning

3. **Eviction Rate**: `rate(cache_evictions_total[5m])`

   - Target: <5%, Alert: >10%

4. **Eviction Reasons**: `rate(cache_evictions_total[5m]) by (reason)`
   - Monitor "Capacity" vs "Expired" vs "Removed"

### Q: How do I create effective dashboards?

**A:** Use this Grafana dashboard structure:

```json
{
  "dashboard": {
    "title": "Cache Performance",
    "panels": [
      {
        "title": "Hit Rate by Cache",
        "type": "stat",
        "targets": [
          {
            "expr": "rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])) by (cache_name)"
          }
        ]
      },
      {
        "title": "Operations Timeline",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(cache_hits_total[5m])",
            "legendFormat": "Hits/sec"
          },
          {
            "expr": "rate(cache_misses_total[5m])",
            "legendFormat": "Misses/sec"
          }
        ]
      }
    ]
  }
}
```

### Q: What alerts should I set up?

**A:** Critical alerts for cache health:

```yaml
# Prometheus Alert Rules
groups:
  - name: cache_health
    rules:
      - alert: CacheHitRateLow
        expr: rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])) < 0.7
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Cache {{ $labels.cache_name }} hit rate below 70%"

      - alert: CacheEvictionRateHigh
        expr: rate(cache_evictions_total[5m]) > 10
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "Cache {{ $labels.cache_name }} evicting >10 items/sec"
```

## Troubleshooting

### Q: My cache metrics aren't appearing in my monitoring system. What's wrong?

**A:** Check these common issues:

1. **Meter Registration**: Ensure the meter is registered and matches the AddMeter() call

   ```csharp
   services.AddSingleton<Meter>(sp => new Meter("MyApp.Cache"));
   // Must match:
   .AddMeter("MyApp.Cache")
   ```

2. **Exporter Configuration**: Verify your exporter is configured correctly

   ```csharp
   .AddOtlpExporter(options =>
   {
       options.Endpoint = new Uri("http://your-collector:4317");
       // Check endpoint reachability
   })
   ```

3. **Cache Usage**: Metrics are only emitted when cache operations occur
   ```csharp
   // Trigger some cache operations to generate metrics
   _cache.Set("test", "value");
   _cache.TryGetValue("test", out _);
   ```

### Q: I'm seeing very low hit rates. How do I diagnose this?

**A:** Follow this diagnostic process:

1. **Verify Cache Behavior**:

   ```csharp
   // Add logging to understand cache patterns
   public bool TryGetValue<T>(object key, out T value)
   {
       var hit = _cache.TryGetValue(key, out value);
       _logger.LogDebug("Cache {Operation} for key {Key}", hit ? "Hit" : "Miss", key);
       return hit;
   }
   ```

2. **Check Eviction Patterns**:

   ```promql
   # Monitor eviction reasons
   rate(cache_evictions_total[5m]) by (reason)
   ```

3. **Analyze Key Patterns**:
   - Are keys consistent between Set and Get operations?
   - Are TTLs too short for your access patterns?
   - Is cache size appropriate for your data set?

### Q: The performance overhead seems higher than expected. How do I investigate?

**A:** Use this troubleshooting approach:

1. **Benchmark Comparison**:

   ```csharp
   [Benchmark(Baseline = true)]
   public bool RawCache_Get() => _rawCache.TryGetValue("key", out _);

   [Benchmark]
   public bool MeteredCache_Get() => _meteredCache.TryGetValue("key", out _);
   ```

2. **Check for High Cardinality**:

   ```csharp
   // Avoid this - creates too many unique metric series
   var options = new MeteredMemoryCacheOptions
   {
       AdditionalTags = { ["user_id"] = userId.ToString() } // High cardinality!
   };
   ```

3. **Profile Memory Usage**:
   ```csharp
   // Monitor for metric-related allocations
   dotMemoryProfiler.StartCollecting();
   // Perform cache operations
   dotMemoryProfiler.SaveData();
   ```

### Q: I'm getting "Collection was modified" exceptions. What's happening?

**A:** This typically occurs with concurrent access to TagList. The error indicates a threading issue that should be resolved in newer versions. Workarounds:

1. **Update to Latest Version**: Ensure you're using the latest MeteredMemoryCache version
2. **Reduce Concurrency**: If possible, reduce concurrent cache access
3. **Report the Issue**: This may indicate a bug that needs fixing

## Integration and Compatibility

### Q: Can I use MeteredMemoryCache with ASP.NET Core?

**A:** Yes! MeteredMemoryCache integrates seamlessly with ASP.NET Core:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add cache with metrics
builder.Services.AddSingleton<Meter>(sp => new Meter("WebApp.Cache"));
builder.Services.AddNamedMeteredMemoryCache("web-cache");

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("WebApp.Cache")
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();
```

### Q: Does MeteredMemoryCache work with dependency injection containers other than Microsoft.Extensions.DependencyInjection?

**A:** Yes, but you'll need to manually register the dependencies:

```csharp
// Autofac example
var builder = new ContainerBuilder();

builder.Register(c => new Meter("MyApp.Cache")).AsSelf().SingleInstance();
builder.Register(c =>
{
    var innerCache = new MemoryCache(new MemoryCacheOptions());
    var meter = c.Resolve<Meter>();
    return new MeteredMemoryCache(innerCache, meter, "main-cache");
}).As<IMemoryCache>().SingleInstance();
```

### Q: Can I use MeteredMemoryCache with .NET Framework?

**A:** MeteredMemoryCache requires .NET 8+ due to its dependencies on:

- `System.Diagnostics.Metrics` (modern metrics API)
- `Microsoft.Extensions.Caching.Memory` (modern IMemoryCache)

For .NET Framework, consider:

- Upgrading to .NET 8+
- Using custom instrumentation with Application Insights or other monitoring
- Implementing a similar pattern with .NET Framework-compatible metrics libraries

### Q: How does MeteredMemoryCache work with Docker and containers?

**A:** MeteredMemoryCache works great in containers. Key considerations:

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
# Ensure proper OTLP configuration
ENV OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
ENV OTEL_SERVICE_NAME=my-service
COPY . .
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

```yaml
# docker-compose.yml
version: "3.8"
services:
  app:
    image: myapp
    environment:
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317

  otel-collector:
    image: otel/opentelemetry-collector:latest
    ports:
      - "4317:4317"
```

## Advanced Scenarios

### Q: How do I implement cache hierarchies with MeteredMemoryCache?

**A:** Create layered caches with different characteristics:

```csharp
public class HierarchicalCacheService
{
    private readonly IMemoryCache _l1Cache; // Fast, small
    private readonly IMemoryCache _l2Cache; // Larger, longer TTL

    public HierarchicalCacheService(
        [FromKeyedServices("l1")] IMemoryCache l1Cache,
        [FromKeyedServices("l2")] IMemoryCache l2Cache)
    {
        _l1Cache = l1Cache;
        _l2Cache = l2Cache;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        // L1 fast lookup
        if (_l1Cache.TryGetValue(key, out T l1Value))
            return l1Value;

        // L2 fallback
        if (_l2Cache.TryGetValue(key, out T l2Value))
        {
            _l1Cache.Set(key, l2Value, TimeSpan.FromMinutes(5));
            return l2Value;
        }

        // Fetch and populate both layers
        var value = await FetchFromSourceAsync<T>(key);
        _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
        _l2Cache.Set(key, value, TimeSpan.FromHours(1));
        return value;
    }
}

// Registration
services.AddNamedMeteredMemoryCache("l1", opt => opt.SizeLimit = 1000);
services.AddNamedMeteredMemoryCache("l2", opt => opt.SizeLimit = 10000);
```

### Q: Can I implement custom eviction tracking beyond the built-in metrics?

**A:** Yes, you can add custom instrumentation:

```csharp
public class CustomMeteredCache : IMemoryCache
{
    private readonly MeteredMemoryCache _inner;
    private readonly Counter<long> _customMetric;

    public CustomMeteredCache(IMemoryCache inner, Meter meter)
    {
        _inner = new MeteredMemoryCache(inner, meter);
        _customMetric = meter.CreateCounter<long>("cache_custom_events");
    }

    public void Set<T>(object key, T value, MemoryCacheEntryOptions options = null)
    {
        options ??= new MemoryCacheEntryOptions();

        // Add custom tracking
        options.RegisterPostEvictionCallback((k, v, reason, state) =>
        {
            _customMetric.Add(1, new KeyValuePair<string, object?>[]
            {
                new("event_type", "custom_eviction"),
                new("key_prefix", GetKeyPrefix(k)),
                new("value_size", EstimateSize(v))
            });
        });

        _inner.Set(key, value, options);
    }

    // Delegate other methods to _inner...
}
```

### Q: How do I implement cache warming strategies with MeteredMemoryCache?

**A:** Use background services for cache warming:

```csharp
public class CacheWarmupService : BackgroundService
{
    private readonly IMemoryCache _cache;
    private readonly IDataService _dataService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await WarmupCriticalData();
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task WarmupCriticalData()
    {
        var criticalData = await _dataService.GetCriticalDataAsync();

        foreach (var item in criticalData)
        {
            _cache.Set($"critical:{item.Id}", item, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.High,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
            });
        }
    }
}
```

## Related Documentation

- [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md) - Basic usage and configuration
- [Best Practices Guide](BestPractices.md) - Recommended patterns and configurations
- [Performance Characteristics](PerformanceCharacteristics.md) - Detailed benchmarks and analysis
- [Troubleshooting Guide](Troubleshooting.md) - Common issues and solutions
- [Migration Guide](MigrationGuide.md) - Step-by-step migration instructions
- [OpenTelemetry Integration](OpenTelemetryIntegration.md) - Metrics setup and monitoring

## Still Have Questions?

If your question isn't answered here:

1. Check the [Troubleshooting Guide](Troubleshooting.md) for common issues
2. Review the [API Reference](ApiReference.md) for detailed method documentation
3. Search existing GitHub issues for similar problems
4. Create a new GitHub issue with:
   - Detailed problem description
   - Code samples demonstrating the issue
   - Expected vs actual behavior
   - Environment details (.NET version, OS, etc.)
