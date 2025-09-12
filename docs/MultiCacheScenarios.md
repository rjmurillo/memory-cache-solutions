# Multi-Cache Scenarios and Naming Conventions

## Overview

MeteredMemoryCache supports sophisticated multi-cache architectures through named cache instances and dimensional metrics. This guide provides patterns, naming conventions, and best practices for implementing multiple cache strategies in production applications.

## Table of Contents

- [Why Multiple Caches?](#why-multiple-caches)
- [Naming Conventions](#naming-conventions)
- [Common Multi-Cache Patterns](#common-multi-cache-patterns)
- [Implementation Strategies](#implementation-strategies)
- [Monitoring and Observability](#monitoring-and-observability)
- [Performance Considerations](#performance-considerations)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Why Multiple Caches?

### Separation of Concerns

Different data types have different caching requirements:

```csharp
// User session data - frequently accessed, medium TTL
services.AddNamedMeteredMemoryCache("user.sessions", opts => {
    opts.AdditionalTags["type"] = "session";
    opts.AdditionalTags["ttl"] = "medium";
});

// Product catalog - read-heavy, long TTL
services.AddNamedMeteredMemoryCache("product.catalog", opts => {
    opts.AdditionalTags["type"] = "catalog";
    opts.AdditionalTags["ttl"] = "long";
});

// API rate limits - short TTL, high frequency
services.AddNamedMeteredMemoryCache("api.ratelimit", opts => {
    opts.AdditionalTags["type"] = "ratelimit";
    opts.AdditionalTags["ttl"] = "short";
});
```

### Different Performance Characteristics

- **Size requirements**: Large datasets vs. small lookup tables
- **Access patterns**: Read-heavy vs. write-heavy vs. mixed
- **TTL strategies**: Static vs. sliding vs. absolute expiration
- **Eviction policies**: LRU vs. size-based vs. time-based

### Operational Benefits

- **Independent monitoring** per cache type
- **Granular capacity planning** and tuning
- **Isolated performance issues** and debugging
- **Separate deployment and configuration** strategies

## Naming Conventions

### Hierarchical Structure

Use dot notation for logical grouping:

```csharp
// Domain-based hierarchy
"user.profile"          // User profile data
"user.preferences"      // User settings and preferences
"user.permissions"      // User authorization data
"user.sessions"         // Active user sessions

"product.catalog"       // Product information
"product.pricing"       // Dynamic pricing data
"product.inventory"     // Stock levels
"product.recommendations" // Personalized recommendations

"content.articles"      // Blog posts and articles
"content.media"         // Images and media files
"content.templates"     // Email/page templates

"api.responses"         // External API response cache
"api.ratelimit"         // Rate limiting counters
"api.auth"             // Authentication tokens
```

### Functional Grouping

```csharp
// By access pattern
"readonly.reference"    // Static reference data
"readonly.config"       // Application configuration
"readwrite.userdata"    // User-generated content
"writeonly.audit"       // Write-through audit logs

// By lifetime
"permanent.config"      // Never expires
"daily.reports"         // Daily expiration
"hourly.stats"          // Hourly expiration
"realtime.feeds"        // Minute-level expiration
```

### Environment-Specific Naming

```csharp
// Include environment context
services.AddNamedMeteredMemoryCache($"user.sessions.{environment}", opts => {
    opts.AdditionalTags["environment"] = environment;
    opts.AdditionalTags["service"] = "auth-service";
});
```

### Anti-Patterns to Avoid

❌ **Don't use these patterns:**

```csharp
"cache1", "cache2"              // Non-descriptive
"temp", "misc", "other"         // Too generic
"userStuff", "apiThings"        // Unclear scope
"fast_cache", "slow_cache"      // Implementation details
"CACHE_USERS_ALL"              // Inconsistent casing
```

## Common Multi-Cache Patterns

### 1. Layered Caching

Implement multiple cache levels with different characteristics:

```csharp
public class LayeredCacheService
{
    private readonly IMemoryCache _l1Cache; // Fast, small capacity
    private readonly IMemoryCache _l2Cache; // Slower, larger capacity
    private readonly IMemoryCache _l3Cache; // Persistent fallback

    public LayeredCacheService(
        [FromKeyedServices("cache.l1")] IMemoryCache l1,
        [FromKeyedServices("cache.l2")] IMemoryCache l2,
        [FromKeyedServices("cache.l3")] IMemoryCache l3)
    {
        _l1Cache = l1;
        _l2Cache = l2;
        _l3Cache = l3;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        // Try L1 first (fastest)
        if (_l1Cache.TryGetValue(key, out T value))
            return value;

        // Fall back to L2
        if (_l2Cache.TryGetValue(key, out value))
        {
            // Promote to L1
            _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
            return value;
        }

        // Fall back to L3
        if (_l3Cache.TryGetValue(key, out value))
        {
            // Promote to L2 and L1
            _l2Cache.Set(key, value, TimeSpan.FromMinutes(30));
            _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));
            return value;
        }

        // Cache miss - load from source
        value = await LoadFromSourceAsync<T>(key);

        // Populate all levels
        _l3Cache.Set(key, value, TimeSpan.FromHours(24));
        _l2Cache.Set(key, value, TimeSpan.FromMinutes(30));
        _l1Cache.Set(key, value, TimeSpan.FromMinutes(5));

        return value;
    }
}

// DI Registration
services.AddNamedMeteredMemoryCache("cache.l1", opts => {
    opts.AdditionalTags["layer"] = "l1";
    opts.AdditionalTags["speed"] = "fast";
}, configure: memOpts => memOpts.SizeLimit = 1000);

services.AddNamedMeteredMemoryCache("cache.l2", opts => {
    opts.AdditionalTags["layer"] = "l2";
    opts.AdditionalTags["speed"] = "medium";
}, configure: memOpts => memOpts.SizeLimit = 10000);

services.AddNamedMeteredMemoryCache("cache.l3", opts => {
    opts.AdditionalTags["layer"] = "l3";
    opts.AdditionalTags["speed"] = "slow";
}, configure: memOpts => memOpts.SizeLimit = 100000);
```

### 2. Domain-Driven Caching

Organize caches by business domain:

```csharp
public class ECommerceModule
{
    public static void ConfigureCaches(IServiceCollection services)
    {
        // Product domain
        services.AddNamedMeteredMemoryCache("product.catalog", opts => {
            opts.AdditionalTags["domain"] = "product";
            opts.AdditionalTags["pattern"] = "read-heavy";
        });

        services.AddNamedMeteredMemoryCache("product.pricing", opts => {
            opts.AdditionalTags["domain"] = "product";
            opts.AdditionalTags["pattern"] = "dynamic";
        });

        // Customer domain
        services.AddNamedMeteredMemoryCache("customer.profile", opts => {
            opts.AdditionalTags["domain"] = "customer";
            opts.AdditionalTags["pii"] = "true";
        });

        services.AddNamedMeteredMemoryCache("customer.preferences", opts => {
            opts.AdditionalTags["domain"] = "customer";
            opts.AdditionalTags["personalization"] = "true";
        });

        // Order domain
        services.AddNamedMeteredMemoryCache("order.cart", opts => {
            opts.AdditionalTags["domain"] = "order";
            opts.AdditionalTags["state"] = "temporary";
        });

        services.AddNamedMeteredMemoryCache("order.history", opts => {
            opts.AdditionalTags["domain"] = "order";
            opts.AdditionalTags["state"] = "permanent";
        });
    }
}
```

### 3. Microservice Cache Federation

Each service manages its own cache domains:

```csharp
// User Service
public class UserServiceCacheConfig
{
    public static void Configure(IServiceCollection services)
    {
        var serviceId = "user-service";

        services.AddNamedMeteredMemoryCache("user.authentication", opts => {
            opts.AdditionalTags["service"] = serviceId;
            opts.AdditionalTags["category"] = "security";
        });

        services.AddNamedMeteredMemoryCache("user.profile", opts => {
            opts.AdditionalTags["service"] = serviceId;
            opts.AdditionalTags["category"] = "data";
        });
    }
}

// Product Service
public class ProductServiceCacheConfig
{
    public static void Configure(IServiceCollection services)
    {
        var serviceId = "product-service";

        services.AddNamedMeteredMemoryCache("product.catalog", opts => {
            opts.AdditionalTags["service"] = serviceId;
            opts.AdditionalTags["category"] = "reference";
        });

        services.AddNamedMeteredMemoryCache("product.search", opts => {
            opts.AdditionalTags["service"] = serviceId;
            opts.AdditionalTags["category"] = "query";
        });
    }
}
```

### 4. Environment-Aware Caching

Different cache configurations per environment:

```csharp
public static class CacheConfiguration
{
    public static void ConfigureForEnvironment(
        IServiceCollection services,
        IHostEnvironment environment)
    {
        var envTags = new Dictionary<string, object?>
        {
            ["environment"] = environment.EnvironmentName,
            ["region"] = Environment.GetEnvironmentVariable("REGION") ?? "unknown"
        };

        if (environment.IsDevelopment())
        {
            ConfigureDevelopmentCaches(services, envTags);
        }
        else if (environment.IsStaging())
        {
            ConfigureStagingCaches(services, envTags);
        }
        else if (environment.IsProduction())
        {
            ConfigureProductionCaches(services, envTags);
        }
    }

    private static void ConfigureDevelopmentCaches(
        IServiceCollection services,
        Dictionary<string, object?> baseTags)
    {
        // Small, fast caches for development
        services.AddNamedMeteredMemoryCache("dev.user.sessions", opts => {
            foreach (var tag in baseTags) opts.AdditionalTags[tag.Key] = tag.Value;
            opts.AdditionalTags["size"] = "small";
        }, configure: memOpts => memOpts.SizeLimit = 100);
    }

    private static void ConfigureProductionCaches(
        IServiceCollection services,
        Dictionary<string, object?> baseTags)
    {
        // Large, optimized caches for production
        services.AddNamedMeteredMemoryCache("prod.user.sessions", opts => {
            foreach (var tag in baseTags) opts.AdditionalTags[tag.Key] = tag.Value;
            opts.AdditionalTags["size"] = "large";
        }, configure: memOpts => memOpts.SizeLimit = 50000);
    }
}
```

## Implementation Strategies

### 1. Cache Factory Pattern

```csharp
public interface ICacheFactory
{
    IMemoryCache GetCache(string name);
    IMemoryCache GetOrCreateCache(string name, Action<MeteredMemoryCacheOptions>? configure = null);
}

public class MeteredCacheFactory : ICacheFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IMemoryCache> _caches;

    public MeteredCacheFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _caches = new ConcurrentDictionary<string, IMemoryCache>();
    }

    public IMemoryCache GetCache(string name)
    {
        return _serviceProvider.GetRequiredKeyedService<IMemoryCache>(name);
    }

    public IMemoryCache GetOrCreateCache(string name, Action<MeteredMemoryCacheOptions>? configure = null)
    {
        return _caches.GetOrAdd(name, cacheName =>
        {
            var innerCache = new MemoryCache(new MemoryCacheOptions());
            var meter = _serviceProvider.GetRequiredService<Meter>();

            var options = new MeteredMemoryCacheOptions { CacheName = cacheName };
            configure?.Invoke(options);

            return new MeteredMemoryCache(innerCache, meter, options);
        });
    }
}
```

### 2. Typed Cache Services

```csharp
public interface IUserCacheService
{
    Task<UserProfile?> GetProfileAsync(int userId);
    Task SetProfileAsync(int userId, UserProfile profile);
    Task<UserSession?> GetSessionAsync(string sessionId);
    Task SetSessionAsync(string sessionId, UserSession session);
}

public class UserCacheService : IUserCacheService
{
    private readonly IMemoryCache _profileCache;
    private readonly IMemoryCache _sessionCache;

    public UserCacheService(
        [FromKeyedServices("user.profile")] IMemoryCache profileCache,
        [FromKeyedServices("user.sessions")] IMemoryCache sessionCache)
    {
        _profileCache = profileCache;
        _sessionCache = sessionCache;
    }

    public async Task<UserProfile?> GetProfileAsync(int userId)
    {
        var key = $"profile:{userId}";
        if (_profileCache.TryGetValue(key, out UserProfile? profile))
            return profile;

        profile = await LoadUserProfileAsync(userId);
        if (profile != null)
        {
            _profileCache.Set(key, profile, TimeSpan.FromMinutes(30));
        }
        return profile;
    }

    public Task SetProfileAsync(int userId, UserProfile profile)
    {
        var key = $"profile:{userId}";
        _profileCache.Set(key, profile, TimeSpan.FromMinutes(30));
        return Task.CompletedTask;
    }

    // Session methods...
}
```

### 3. Configuration-Driven Setup

```json
{
  "Caching": {
    "Caches": [
      {
        "Name": "user.profile",
        "SizeLimit": 10000,
        "DefaultExpiration": "00:30:00",
        "Tags": {
          "domain": "user",
          "type": "profile"
        }
      },
      {
        "Name": "product.catalog",
        "SizeLimit": 50000,
        "DefaultExpiration": "02:00:00",
        "Tags": {
          "domain": "product",
          "type": "catalog"
        }
      }
    ]
  }
}
```

```csharp
public class CacheConfig
{
    public List<CacheDefinition> Caches { get; set; } = new();
}

public class CacheDefinition
{
    public string Name { get; set; } = string.Empty;
    public int? SizeLimit { get; set; }
    public TimeSpan? DefaultExpiration { get; set; }
    public Dictionary<string, object?> Tags { get; set; } = new();
}

public static class CacheConfigurationExtensions
{
    public static IServiceCollection AddConfiguredCaches(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cacheConfig = configuration.GetSection("Caching").Get<CacheConfig>();

        foreach (var cache in cacheConfig?.Caches ?? [])
        {
            services.AddNamedMeteredMemoryCache(cache.Name, opts =>
            {
                foreach (var tag in cache.Tags)
                {
                    opts.AdditionalTags[tag.Key] = tag.Value;
                }
            }, configure: memOpts =>
            {
                if (cache.SizeLimit.HasValue)
                    memOpts.SizeLimit = cache.SizeLimit.Value;
            });
        }

        return services;
    }
}
```

## Monitoring and Observability

### Grafana Dashboard Queries

```promql
# Cache hit rate by domain
rate(cache_hits_total[5m]) /
(rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))
* 100

# Top cache consumers by operation rate
topk(10,
  rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])
) by (cache_name)

# Eviction rate by cache and reason
rate(cache_evictions_total[5m]) by (cache_name, reason)

# Cache efficiency comparison
(
  rate(cache_hits_total{cache_name=~"user.*"}[5m]) /
  (rate(cache_hits_total{cache_name=~"user.*"}[5m]) +
   rate(cache_misses_total{cache_name=~"user.*"}[5m]))
) * 100
```

### Custom Metrics Aggregation

```csharp
public class CacheMetricsService
{
    private readonly IMetricsLogger<CacheMetricsService> _metrics;

    public CacheMetricsService(IMetricsLogger<CacheMetricsService> metrics)
    {
        _metrics = metrics;
    }

    public void RecordDomainMetrics(string domain, string operation, TimeSpan duration)
    {
        _metrics.LogValue("cache.domain.operation.duration", duration.TotalMilliseconds,
            ("domain", domain), ("operation", operation));
    }

    public void RecordCachePattern(string pattern, bool hit)
    {
        _metrics.LogValue("cache.pattern.hits", hit ? 1 : 0,
            ("pattern", pattern), ("result", hit ? "hit" : "miss"));
    }
}
```

### Alerting Rules

```yaml
# Prometheus alerting rules
groups:
  - name: cache.rules
    rules:
      - alert: LowCacheHitRate
        expr: |
          (
            rate(cache_hits_total[5m]) / 
            (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))
          ) < 0.5
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Low cache hit rate for {{ $labels.cache_name }}"
          description: "Cache {{ $labels.cache_name }} hit rate is {{ $value | humanizePercentage }}"

      - alert: HighEvictionRate
        expr: rate(cache_evictions_total[5m]) > 10
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "High eviction rate for {{ $labels.cache_name }}"
          description: "Cache {{ $labels.cache_name }} evicting {{ $value }} items/sec"

      - alert: CacheSpecificHitRate
        expr: |
          (
            rate(cache_hits_total{cache_name=~"user\..*"}[5m]) / 
            (rate(cache_hits_total{cache_name=~"user\..*"}[5m]) + 
             rate(cache_misses_total{cache_name=~"user\..*"}[5m]))
          ) < 0.7
        for: 5m
        labels:
          severity: critical
          domain: user
        annotations:
          summary: "Critical hit rate for user domain caches"
```

## Performance Considerations

### Cache Sizing Guidelines

```csharp
public static class CacheSizingGuidelines
{
    public static void ConfigureByDomain(IServiceCollection services)
    {
        // High-frequency, small objects
        services.AddNamedMeteredMemoryCache("user.sessions", opts => {
            opts.AdditionalTags["frequency"] = "high";
            opts.AdditionalTags["object_size"] = "small";
        }, configure: memOpts => memOpts.SizeLimit = 100000);

        // Medium-frequency, medium objects
        services.AddNamedMeteredMemoryCache("product.catalog", opts => {
            opts.AdditionalTags["frequency"] = "medium";
            opts.AdditionalTags["object_size"] = "medium";
        }, configure: memOpts => memOpts.SizeLimit = 10000);

        // Low-frequency, large objects
        services.AddNamedMeteredMemoryCache("content.media", opts => {
            opts.AdditionalTags["frequency"] = "low";
            opts.AdditionalTags["object_size"] = "large";
        }, configure: memOpts => memOpts.SizeLimit = 1000);
    }
}
```

### Memory Pressure Handling

```csharp
public class AdaptiveCacheManager
{
    private readonly Dictionary<string, IMemoryCache> _caches;
    private readonly IMemoryMonitor _memoryMonitor;

    public AdaptiveCacheManager(ICacheFactory cacheFactory, IMemoryMonitor memoryMonitor)
    {
        _caches = new Dictionary<string, IMemoryCache>();
        _memoryMonitor = memoryMonitor;

        _memoryMonitor.HighPressure += OnHighMemoryPressure;
        _memoryMonitor.LowPressure += OnLowMemoryPressure;
    }

    private void OnHighMemoryPressure(object? sender, EventArgs e)
    {
        // Reduce cache sizes during memory pressure
        foreach (var cache in _caches.Values)
        {
            if (cache is MemoryCache memCache)
            {
                memCache.Compact(0.25); // Remove 25% of entries
            }
        }
    }

    private void OnLowMemoryPressure(object? sender, EventArgs e)
    {
        // Can increase cache sizes when memory is available
        // Implementation depends on cache framework capabilities
    }
}
```

## Best Practices

### 1. Naming Standards

```csharp
public static class CacheNames
{
    // Use constants for cache names
    public const string UserProfiles = "user.profiles";
    public const string UserSessions = "user.sessions";
    public const string ProductCatalog = "product.catalog";
    public const string ProductPricing = "product.pricing";

    // Helper methods for dynamic names
    public static string UserProfileById(int userId) => $"{UserProfiles}:{userId}";
    public static string ProductByCategory(string category) => $"{ProductCatalog}:category:{category}";
}

// Usage
services.AddNamedMeteredMemoryCache(CacheNames.UserProfiles, opts => {
    opts.AdditionalTags["domain"] = "user";
});

// In application code
var userCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>(CacheNames.UserProfiles);
```

### 2. Tagging Strategy

```csharp
public static class CacheTags
{
    // Standard tag keys
    public const string Domain = "domain";
    public const string Type = "type";
    public const string Environment = "environment";
    public const string Service = "service";
    public const string Region = "region";
    public const string Version = "version";

    // Domain values
    public const string UserDomain = "user";
    public const string ProductDomain = "product";
    public const string ContentDomain = "content";

    // Type values
    public const string ProfileType = "profile";
    public const string SessionType = "session";
    public const string CatalogType = "catalog";
}

// Consistent tagging
services.AddNamedMeteredMemoryCache("user.profiles", opts => {
    opts.AdditionalTags[CacheTags.Domain] = CacheTags.UserDomain;
    opts.AdditionalTags[CacheTags.Type] = CacheTags.ProfileType;
    opts.AdditionalTags[CacheTags.Environment] = environment.EnvironmentName;
});
```

### 3. Lifecycle Management

```csharp
public class CacheLifecycleManager : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheLifecycleManager> _logger;
    private readonly Dictionary<string, IMemoryCache> _managedCaches;

    public CacheLifecycleManager(IServiceProvider serviceProvider, ILogger<CacheLifecycleManager> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _managedCaches = new Dictionary<string, IMemoryCache>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing managed caches");

        // Pre-warm critical caches
        PrewarmCache("user.sessions");
        PrewarmCache("product.catalog");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disposing managed caches");

        foreach (var cache in _managedCaches.Values)
        {
            cache.Dispose();
        }

        return Task.CompletedTask;
    }

    private void PrewarmCache(string cacheName)
    {
        try
        {
            var cache = _serviceProvider.GetRequiredKeyedService<IMemoryCache>(cacheName);
            _managedCaches[cacheName] = cache;

            // Add pre-warming logic here
            _logger.LogInformation("Cache {CacheName} pre-warmed", cacheName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pre-warm cache {CacheName}", cacheName);
        }
    }
}
```

## Troubleshooting

### Common Issues

#### 1. Cache Name Conflicts

```csharp
// Problem: Duplicate cache registrations
services.AddNamedMeteredMemoryCache("user.cache", opts => { });
services.AddNamedMeteredMemoryCache("user.cache", opts => { }); // Conflict!

// Solution: Use validation
public static class CacheValidation
{
    private static readonly HashSet<string> RegisteredCaches = new();

    public static IServiceCollection AddValidatedCache(
        this IServiceCollection services,
        string cacheName,
        Action<MeteredMemoryCacheOptions>? configure = null)
    {
        if (!RegisteredCaches.Add(cacheName))
        {
            throw new InvalidOperationException($"Cache '{cacheName}' is already registered");
        }

        return services.AddNamedMeteredMemoryCache(cacheName, configure);
    }
}
```

#### 2. Memory Pressure

```csharp
// Monitor cache memory usage
public class CacheMemoryMonitor
{
    private readonly ILogger<CacheMemoryMonitor> _logger;
    private readonly Timer _timer;

    public CacheMemoryMonitor(ILogger<CacheMemoryMonitor> logger)
    {
        _logger = logger;
        _timer = new Timer(CheckMemoryUsage, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    private void CheckMemoryUsage(object? state)
    {
        var totalMemory = GC.GetTotalMemory(false);
        var gen2Collections = GC.CollectionCount(2);

        if (totalMemory > 500_000_000) // 500MB threshold
        {
            _logger.LogWarning("High memory usage detected: {MemoryMB}MB, Gen2 Collections: {Gen2}",
                totalMemory / (1024 * 1024), gen2Collections);
        }
    }
}
```

#### 3. Debugging Cache Misses

```csharp
public class CacheDebugService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheDebugService> _logger;

    public CacheDebugService(
        [FromKeyedServices("debug.cache")] IMemoryCache cache,
        ILogger<CacheDebugService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public T? GetWithLogging<T>(string key)
    {
        var hit = _cache.TryGetValue(key, out T? value);

        _logger.LogDebug("Cache {Result} for key: {Key}, Type: {Type}",
            hit ? "HIT" : "MISS", key, typeof(T).Name);

        if (!hit)
        {
            _logger.LogDebug("Cache miss details - Key: {Key}, Cache: {CacheName}",
                key, "debug.cache");
        }

        return value;
    }
}
```

### Performance Analysis

```csharp
public class CachePerformanceAnalyzer
{
    private readonly Dictionary<string, CacheStats> _stats = new();

    public void RecordOperation(string cacheName, string operation, bool hit, TimeSpan duration)
    {
        if (!_stats.TryGetValue(cacheName, out var stats))
        {
            stats = new CacheStats(cacheName);
            _stats[cacheName] = stats;
        }

        stats.RecordOperation(operation, hit, duration);
    }

    public void LogReport(ILogger logger)
    {
        foreach (var (cacheName, stats) in _stats)
        {
            logger.LogInformation(
                "Cache {CacheName}: Hit Rate: {HitRate:P2}, Avg Duration: {AvgDuration}ms, Operations: {TotalOps}",
                cacheName, stats.HitRate, stats.AverageDuration.TotalMilliseconds, stats.TotalOperations);
        }
    }
}

public class CacheStats
{
    public string CacheName { get; }
    public long TotalHits { get; private set; }
    public long TotalMisses { get; private set; }
    public long TotalOperations => TotalHits + TotalMisses;
    public double HitRate => TotalOperations > 0 ? (double)TotalHits / TotalOperations : 0;
    public TimeSpan AverageDuration { get; private set; }

    private TimeSpan _totalDuration;

    public CacheStats(string cacheName)
    {
        CacheName = cacheName;
    }

    public void RecordOperation(string operation, bool hit, TimeSpan duration)
    {
        if (hit) TotalHits++; else TotalMisses++;

        _totalDuration += duration;
        AverageDuration = TimeSpan.FromTicks(_totalDuration.Ticks / TotalOperations);
    }
}
```

## Conclusion

Multi-cache scenarios require careful planning and consistent patterns. Key takeaways:

1. **Use hierarchical naming** with dot notation for logical organization
2. **Implement consistent tagging** strategies for observability
3. **Separate concerns** by domain, access pattern, and lifecycle
4. **Monitor performance** with dedicated dashboards and alerts
5. **Plan for scale** with adaptive sizing and memory pressure handling
6. **Follow naming conventions** to prevent conflicts and confusion

For more information, see:

- [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md)
- [OpenTelemetry Integration Guide](OpenTelemetryIntegration.md)
