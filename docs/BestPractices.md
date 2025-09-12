# Best Practices Guide

This document consolidates best practices for using MeteredMemoryCache effectively, covering performance optimization, configuration patterns, monitoring strategies, and common pitfalls to avoid.

## Table of Contents

- [Configuration Best Practices](#configuration-best-practices)
- [Performance Optimization](#performance-optimization)
- [Monitoring and Observability](#monitoring-and-observability)
- [Multi-Cache Architecture](#multi-cache-architecture)
- [Security Considerations](#security-considerations)
- [Testing Strategies](#testing-strategies)
- [Production Deployment](#production-deployment)
- [Common Pitfalls and Solutions](#common-pitfalls-and-solutions)

## Configuration Best Practices

### Cache Naming Conventions

Use consistent, descriptive names that reflect the cache's purpose:

✅ **Good Examples**:

```csharp
services.AddNamedMeteredMemoryCache("user-sessions");
services.AddNamedMeteredMemoryCache("product-catalog");
services.AddNamedMeteredMemoryCache("api-responses");
services.AddNamedMeteredMemoryCache("static-content");
```

❌ **Avoid**:

```csharp
services.AddNamedMeteredMemoryCache("cache1");
services.AddNamedMeteredMemoryCache("data");
services.AddNamedMeteredMemoryCache("temp");
```

### Naming Guidelines

- Use kebab-case for consistency with metric conventions
- Include the data type or domain (e.g., `user-profiles`, `order-history`)
- Avoid generic names that don't describe the content
- Keep names under 50 characters for dashboard readability
- Use environment prefixes in multi-environment deployments: `prod-user-cache`

### Memory Size Configuration

Configure appropriate size limits based on your application's memory profile:

```csharp
services.AddNamedMeteredMemoryCache("large-objects", options =>
{
    options.SizeLimit = 100;           // For large objects (images, documents)
    options.CompactionPercentage = 0.1; // Aggressive cleanup
});

services.AddNamedMeteredMemoryCache("small-frequent", options =>
{
    options.SizeLimit = 10000;         // For small, frequent objects
    options.CompactionPercentage = 0.25; // Standard cleanup
});
```

### Size Calculation Guidelines

| Object Type                           | Estimated Size | Recommended Limit    |
| ------------------------------------- | -------------- | -------------------- |
| **Small Objects** (primitives, DTOs)  | 100B - 1KB     | 5,000 - 50,000 items |
| **Medium Objects** (rich entities)    | 1KB - 10KB     | 500 - 5,000 items    |
| **Large Objects** (images, documents) | 10KB+          | 50 - 500 items       |

### Expiration Strategies

Choose appropriate expiration patterns based on data characteristics:

```csharp
// Static Reference Data - Long expiration
_cache.Set("countries", countries, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
    Priority = CacheItemPriority.High
});

// User Session Data - Sliding expiration
_cache.Set($"session:{userId}", sessionData, new MemoryCacheEntryOptions
{
    SlidingExpiration = TimeSpan.FromMinutes(30),
    Priority = CacheItemPriority.Normal
});

// API Response Cache - Short, absolute expiration
_cache.Set($"api-response:{key}", response, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    Priority = CacheItemPriority.Low
});
```

## Performance Optimization

### Minimize Allocation Overhead

Use efficient patterns to reduce memory allocations:

✅ **Efficient Pattern**:

```csharp
// Pre-calculate keys to avoid string concatenation
private readonly string _keyPrefix = $"user:{Environment.MachineName}:";

public User GetUser(int id)
{
    var key = _keyPrefix + id.ToString(); // Single allocation
    return _cache.GetOrCreate(key, entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(15);
        return _userRepository.GetUser(id);
    });
}
```

❌ **Inefficient Pattern**:

```csharp
public User GetUser(int id)
{
    // Multiple allocations on each call
    var key = $"user:{Environment.MachineName}:{DateTime.Now:yyyy-MM-dd}:{id}";
    return _cache.GetOrCreate(key, entry => _userRepository.GetUser(id));
}
```

### Batch Operations for Better Performance

When possible, use batch patterns to amortize overhead:

```csharp
public async Task<Dictionary<int, User>> GetUsersAsync(IEnumerable<int> userIds)
{
    var results = new Dictionary<int, User>();
    var missingIds = new List<int>();

    // Check cache for all IDs first
    foreach (var id in userIds)
    {
        if (_cache.TryGetValue($"user:{id}", out User cached))
        {
            results[id] = cached;
        }
        else
        {
            missingIds.Add(id);
        }
    }

    // Batch fetch missing users
    if (missingIds.Any())
    {
        var users = await _userRepository.GetUsersAsync(missingIds);
        foreach (var user in users)
        {
            _cache.Set($"user:{user.Id}", user, TimeSpan.FromMinutes(15));
            results[user.Id] = user;
        }
    }

    return results;
}
```

### Key Design Patterns

Design cache keys for optimal performance and collision avoidance:

```csharp
// Hierarchical key structure
public static class CacheKeys
{
    public static string UserProfile(int userId) => $"user:profile:{userId}";
    public static string UserSessions(int userId) => $"user:sessions:{userId}";
    public static string ProductsByCategory(string category) => $"product:category:{category}";
    public static string ApiResponse(string endpoint, string version) => $"api:{endpoint}:v{version}";
}
```

### Thread-Safe Patterns

Use thread-safe patterns for high-concurrency scenarios:

```csharp
public class ThreadSafeCacheService
{
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // Use GetOrCreate for thread-safe lazy initialization
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        return await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(15);

            // Ensure only one thread executes the factory
            await _semaphore.WaitAsync();
            try
            {
                return await factory();
            }
            finally
            {
                _semaphore.Release();
            }
        });
    }
}
```

## Monitoring and Observability

### Essential Metrics to Monitor

Track these key metrics in production:

```csharp
// Set up metric collection for monitoring
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddView("cache_hit_ratio", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = new[] { 0.5, 0.7, 0.8, 0.9, 0.95, 0.99 }
        }));
```

### Key Performance Indicators (KPIs)

| Metric                | Target Range   | Alert Threshold |
| --------------------- | -------------- | --------------- |
| **Hit Rate**          | > 80%          | < 70%           |
| **Miss Rate**         | < 20%          | > 30%           |
| **Eviction Rate**     | < 5%           | > 10%           |
| **Operation Latency** | < 100ns        | > 500ns         |
| **Memory Usage**      | < 80% of limit | > 90%           |

### Dashboard Configuration

Create comprehensive dashboards with these panels:

```yaml
# Grafana Dashboard Configuration
Cache_Performance:
  panels:
    - title: "Hit/Miss Rate"
      query: "rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))"

    - title: "Cache Operations/sec"
      query: "rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])"

    - title: "Evictions by Reason"
      query: "rate(cache_evictions_total[5m])"
      group_by: "reason"

    - title: "Cache Performance by Name"
      query: "rate(cache_hits_total[5m])"
      group_by: "cache_name"
```

### Alerting Rules

Set up proactive alerts for cache health:

```yaml
# Prometheus Alert Rules
groups:
  - name: cache_alerts
    rules:
      - alert: CacheHitRateLow
        expr: rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])) < 0.7
        for: 5m
        annotations:
          summary: "Cache hit rate is below 70%"

      - alert: CacheEvictionRateHigh
        expr: rate(cache_evictions_total[5m]) > 10
        for: 2m
        annotations:
          summary: "Cache eviction rate is above 10/sec"
```

## Multi-Cache Architecture

### Cache Hierarchy Design

Implement layered caching for optimal performance:

```csharp
public class HierarchicalCacheService
{
    private readonly IMemoryCache _l1Cache; // Fast, small
    private readonly IMemoryCache _l2Cache; // Larger, longer TTL

    public async Task<T> GetAsync<T>(string key)
    {
        // L1 Cache - Fast lookup
        if (_l1Cache.TryGetValue(key, out T l1Value))
        {
            return l1Value;
        }

        // L2 Cache - Secondary lookup
        if (_l2Cache.TryGetValue(key, out T l2Value))
        {
            // Promote to L1
            _l1Cache.Set(key, l2Value, TimeSpan.FromMinutes(5));
            return l2Value;
        }

        // Cache miss - fetch from source
        var value = await FetchFromSourceAsync<T>(key);

        // Store in both layers
        _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
        _l2Cache.Set(key, value, TimeSpan.FromHours(1));

        return value;
    }
}
```

### Domain-Specific Cache Separation

Separate caches by domain for better isolation:

```csharp
// Domain-specific cache configuration
services.AddNamedMeteredMemoryCache("user-data", options =>
{
    options.SizeLimit = 10000;
    options.CompactionPercentage = 0.25;
});

services.AddNamedMeteredMemoryCache("product-catalog", options =>
{
    options.SizeLimit = 5000;
    options.CompactionPercentage = 0.1; // Less aggressive for reference data
});

services.AddNamedMeteredMemoryCache("session-store", options =>
{
    options.SizeLimit = 50000;
    options.CompactionPercentage = 0.5; // More aggressive for volatile data
});
```

### Cache Coordination Patterns

Use patterns to maintain consistency across multiple caches:

```csharp
public class CoordinatedCacheService
{
    private readonly IMemoryCache _userCache;
    private readonly IMemoryCache _profileCache;

    public async Task InvalidateUserAsync(int userId)
    {
        // Coordinate invalidation across related caches
        var tasks = new[]
        {
            Task.Run(() => _userCache.Remove($"user:{userId}")),
            Task.Run(() => _profileCache.Remove($"profile:{userId}")),
            Task.Run(() => InvalidateUserPermissions(userId))
        };

        await Task.WhenAll(tasks);
    }
}
```

## Security Considerations

### Cache Key Security

Avoid exposing sensitive information in cache keys:

✅ **Secure Pattern**:

```csharp
// Use hashed keys for sensitive data
public string CreateSecureKey(string sensitiveData)
{
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sensitiveData));
    return $"secure:{Convert.ToBase64String(hash)[..16]}";
}
```

❌ **Insecure Pattern**:

```csharp
// Never expose PII or sensitive data in keys
var key = $"user-cache:{user.SocialSecurityNumber}:{user.Email}";
```

### Data Sanitization

Ensure cached data doesn't contain sensitive information in metric tags:

```csharp
public class SecureCacheOptions : MeteredMemoryCacheOptions
{
    public SecureCacheOptions()
    {
        // Only add non-sensitive dimensional tags
        AdditionalTags["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        AdditionalTags["region"] = Environment.GetEnvironmentVariable("REGION");
        // Never add user IDs, emails, or other PII
    }
}
```

### Access Control Patterns

Implement cache access controls for multi-tenant scenarios:

```csharp
public class TenantAwareCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;

    public T Get<T>(string key)
    {
        var tenantKey = $"tenant:{_tenantContext.TenantId}:{key}";
        return _cache.Get<T>(tenantKey);
    }

    public void Set<T>(string key, T value, TimeSpan expiry)
    {
        var tenantKey = $"tenant:{_tenantContext.TenantId}:{key}";
        _cache.Set(tenantKey, value, expiry);
    }
}
```

## Testing Strategies

### Unit Testing Cache Behavior

Test cache interactions with proper isolation:

```csharp
[Test]
public void Cache_StoresAndRetrievesValues()
{
    // Arrange
    var cache = new MemoryCache(new MemoryCacheOptions());
    var meter = new Meter("test");
    var meteredCache = new MeteredMemoryCache(cache, meter, "test-cache");

    // Act
    meteredCache.Set("key", "value", TimeSpan.FromMinutes(1));
    var retrieved = meteredCache.TryGetValue("key", out string result);

    // Assert
    Assert.IsTrue(retrieved);
    Assert.AreEqual("value", result);
}
```

### Integration Testing with Metrics

Validate metric emission in integration tests:

```csharp
[Test]
public async Task Cache_EmitsCorrectMetrics()
{
    // Arrange
    using var meterProvider = Sdk.CreateMeterProviderBuilder()
        .AddMeter("test")
        .Build();

    var exportedItems = new List<Metric>();
    using var meter = new Meter("test");
    var cache = new MeteredMemoryCache(new MemoryCache(new MemoryCacheOptions()), meter);

    // Act
    cache.Set("key", "value");
    cache.TryGetValue("key", out _); // Hit
    cache.TryGetValue("missing", out _); // Miss

    // Assert
    // Verify hit and miss metrics were emitted
    Assert.That(exportedItems, Has.Some.Property("Name").EqualTo("cache_hits_total"));
    Assert.That(exportedItems, Has.Some.Property("Name").EqualTo("cache_misses_total"));
}
```

### Performance Testing

Include cache performance in benchmark suites:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CachePerformanceBenchmark
{
    private IMemoryCache _rawCache;
    private IMemoryCache _meteredCache;

    [GlobalSetup]
    public void Setup()
    {
        _rawCache = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("benchmark");
        _meteredCache = new MeteredMemoryCache(_rawCache, meter);
    }

    [Benchmark(Baseline = true)]
    public bool RawCache_TryGet() => _rawCache.TryGetValue("key", out _);

    [Benchmark]
    public bool MeteredCache_TryGet() => _meteredCache.TryGetValue("key", out _);
}
```

## Production Deployment

### Gradual Rollout Strategy

Deploy MeteredMemoryCache incrementally:

```csharp
// Phase 1: Enable for 10% of traffic
services.AddSingleton<IMemoryCache>(sp =>
{
    var cache = new MemoryCache(new MemoryCacheOptions());

    if (ShouldEnableMetrics(0.1)) // 10% chance
    {
        var meter = sp.GetRequiredService<Meter>();
        return new MeteredMemoryCache(cache, meter, "gradual-rollout");
    }

    return cache;
});
```

### Environment-Specific Configuration

Configure different settings per environment:

```csharp
// appsettings.Production.json
{
  "Cache": {
    "SizeLimit": 50000,
    "CompactionPercentage": 0.1,
    "MetricsEnabled": true,
    "MeterName": "Production.Cache"
  }
}

// appsettings.Development.json
{
  "Cache": {
    "SizeLimit": 1000,
    "CompactionPercentage": 0.5,
    "MetricsEnabled": true,
    "MeterName": "Development.Cache"
  }
}
```

### Monitoring Setup

Configure comprehensive monitoring for production:

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Production.Cache")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("https://otel-collector.company.com");
            options.Headers = "api-key=production-key";
        })
        .AddConsoleExporter()); // Fallback for debugging
```

### Health Checks

Include cache health in application health checks:

```csharp
services.AddHealthChecks()
    .AddCheck<CacheHealthCheck>("cache-health")
    .AddCheck("cache-metrics", () =>
    {
        // Verify metrics are being exported
        return _metricsExporter.IsHealthy()
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Metrics export issues");
    });
```

## Common Pitfalls and Solutions

### Pitfall 1: Tag Cardinality Explosion

❌ **Problem**: Adding high-cardinality data to tags

```csharp
// DON'T: This creates millions of unique metric series
var options = new MeteredMemoryCacheOptions
{
    AdditionalTags =
    {
        ["user_id"] = userId.ToString(), // High cardinality!
        ["timestamp"] = DateTime.Now.ToString() // Infinite cardinality!
    }
};
```

✅ **Solution**: Use low-cardinality dimensional tags

```csharp
var options = new MeteredMemoryCacheOptions
{
    AdditionalTags =
    {
        ["cache_type"] = "user-data", // Low cardinality
        ["region"] = "us-west",       // Low cardinality
        ["environment"] = "production" // Low cardinality
    }
};
```

### Pitfall 2: Inappropriate Cache Sizes

❌ **Problem**: One-size-fits-all cache configuration

```csharp
// DON'T: Same settings for all cache types
services.AddNamedMeteredMemoryCache("user-cache", options => options.SizeLimit = 1000);
services.AddNamedMeteredMemoryCache("large-files", options => options.SizeLimit = 1000); // Wrong!
```

✅ **Solution**: Size caches based on object characteristics

```csharp
services.AddNamedMeteredMemoryCache("user-profiles", options =>
{
    options.SizeLimit = 10000; // Small objects, many items
    options.CompactionPercentage = 0.25;
});

services.AddNamedMeteredMemoryCache("document-cache", options =>
{
    options.SizeLimit = 100; // Large objects, few items
    options.CompactionPercentage = 0.1;
});
```

### Pitfall 3: Ignoring Eviction Patterns

❌ **Problem**: Not monitoring or responding to evictions

```csharp
// DON'T: Set and forget
_cache.Set(key, largeObject, TimeSpan.FromHours(24)); // May be evicted due to pressure
```

✅ **Solution**: Monitor eviction metrics and adjust strategy

```csharp
// Monitor eviction rates and adjust TTL accordingly
var options = new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), // Shorter TTL
    Priority = CacheItemPriority.High, // For critical data
    Size = EstimateObjectSize(data) // Help with eviction decisions
};
_cache.Set(key, data, options);
```

### Pitfall 4: Synchronous Operations in Async Context

❌ **Problem**: Blocking async context with synchronous cache operations

```csharp
public async Task<User> GetUserAsync(int id)
{
    // DON'T: This can cause deadlocks
    return _cache.GetOrCreate($"user:{id}", entry =>
    {
        return _userService.GetUserAsync(id).Result; // Blocking!
    });
}
```

✅ **Solution**: Use async patterns consistently

```csharp
public async Task<User> GetUserAsync(int id)
{
    return await _cache.GetOrCreateAsync($"user:{id}", async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(15);
        return await _userService.GetUserAsync(id);
    });
}
```

## Related Documentation

- [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md) - Basic usage and configuration
- [Performance Characteristics](PerformanceCharacteristics.md) - Detailed performance analysis
- [Migration Guide](MigrationGuide.md) - Step-by-step migration instructions
- [Troubleshooting Guide](Troubleshooting.md) - Common issues and solutions
- [OpenTelemetry Integration](OpenTelemetryIntegration.md) - Metrics setup and monitoring
- [Multi-Cache Scenarios](MultiCacheScenarios.md) - Advanced multi-cache patterns
