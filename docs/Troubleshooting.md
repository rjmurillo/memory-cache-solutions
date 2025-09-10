# MeteredMemoryCache Troubleshooting Guide

## Overview

This guide provides solutions for common configuration issues, debugging techniques, and troubleshooting steps for MeteredMemoryCache implementations. Use this guide to diagnose and resolve problems with cache metrics, service registration, and OpenTelemetry integration.

## Common Configuration Issues

### 1. No Metrics Being Emitted

**Symptom**: Cache operations are working but no metrics appear in your monitoring system.

**Possible Causes & Solutions**:

#### Missing Meter Registration

```csharp
// ❌ Problem: Meter not registered
services.AddNamedMeteredMemoryCache("user-cache");

// ✅ Solution: Ensure Meter is registered
services.AddSingleton<Meter>(sp => new Meter("MyApp.Cache"));
services.AddNamedMeteredMemoryCache("user-cache");
```

#### OpenTelemetry Not Configured

```csharp
// ❌ Problem: Missing OpenTelemetry setup
services.AddNamedMeteredMemoryCache("user-cache");

// ✅ Solution: Add OpenTelemetry metrics
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")  // Must match meter name
        .AddOtlpExporter());
```

#### Incorrect Meter Name Mismatch

```csharp
// ❌ Problem: Meter names don't match
services.AddSingleton<Meter>(sp => new Meter("AppCache"));
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("MyApp.Cache")); // Different name!

// ✅ Solution: Use consistent meter names
const string METER_NAME = "MyApp.Cache";
services.AddSingleton<Meter>(sp => new Meter(METER_NAME));
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(METER_NAME));
```

### 2. Service Registration Failures

**Symptom**: `InvalidOperationException` or dependency injection errors during startup.

#### Multiple Cache Registrations Conflict

```csharp
// ❌ Problem: Conflicting cache registrations
services.AddMemoryCache();
services.AddNamedMeteredMemoryCache("cache1");
services.AddNamedMeteredMemoryCache("cache2"); // May conflict with first registration

// ✅ Solution: Use keyed services for multiple caches
services.AddKeyedSingleton<IMemoryCache>("cache1", (sp, key) =>
    sp.GetRequiredKeyedService<IMemoryCache>("cache1"));
services.AddKeyedSingleton<IMemoryCache>("cache2", (sp, key) =>
    sp.GetRequiredKeyedService<IMemoryCache>("cache2"));
```

#### Decoration Without Base Cache

```csharp
// ❌ Problem: Trying to decorate non-existent cache
services.DecorateMemoryCacheWithMetrics("my-cache");

// ✅ Solution: Register base cache first
services.AddMemoryCache();
services.DecorateMemoryCacheWithMetrics("my-cache");
```

#### Missing Required Dependencies

```csharp
// ❌ Problem: Missing options or validation services
services.AddNamedMeteredMemoryCache("cache");
// Missing: Options framework, validation, etc.

// ✅ Solution: Ensure all dependencies are registered
services.AddOptions(); // Required for options pattern
services.AddNamedMeteredMemoryCache("cache");
```

### 3. Options Validation Failures

**Symptom**: `OptionsValidationException` during cache creation or service startup.

#### Invalid Cache Names

```csharp
// ❌ Problem: Invalid cache name
services.AddNamedMeteredMemoryCache(""); // Empty string
services.AddNamedMeteredMemoryCache("   "); // Whitespace
services.AddNamedMeteredMemoryCache(null!); // Null

// ✅ Solution: Use valid cache names
services.AddNamedMeteredMemoryCache("user-cache");
services.AddNamedMeteredMemoryCache("product.catalog");
services.AddNamedMeteredMemoryCache("session_store");
```

#### Invalid Additional Tags

```csharp
// ❌ Problem: Invalid tag configuration
services.AddNamedMeteredMemoryCache("cache", options =>
{
    options.AdditionalTags.Add("", "value"); // Empty key
    options.AdditionalTags.Add("key", null!); // Null value
    options.AdditionalTags.Add("cache.name", "override"); // Reserved key
});

// ✅ Solution: Use valid tag keys and values
services.AddNamedMeteredMemoryCache("cache", options =>
{
    options.AdditionalTags.Add("environment", "production");
    options.AdditionalTags.Add("region", "us-east-1");
    options.AdditionalTags.Add("version", "v1.2.3");
});
```

### 4. Performance Issues

**Symptom**: Unexpected performance degradation after adding MeteredMemoryCache.

#### Excessive Tag Creation

```csharp
// ❌ Problem: Too many dynamic tags
services.AddNamedMeteredMemoryCache("cache", options =>
{
    // Adding 50+ tags will impact performance
    for (int i = 0; i < 100; i++)
    {
        options.AdditionalTags.Add($"tag{i}", $"value{i}");
    }
});

// ✅ Solution: Limit to essential tags
services.AddNamedMeteredMemoryCache("cache", options =>
{
    options.AdditionalTags.Add("environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
    options.AdditionalTags.Add("instance", Environment.MachineName);
    // Keep to 5-10 tags maximum
});
```

#### Memory Pressure from Disposal

```csharp
// ❌ Problem: Disposing inner cache when it shouldn't be
services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));
services.DecorateMemoryCacheWithMetrics("cache"); // Will dispose the singleton!

// ✅ Solution: Control disposal behavior
services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));
services.DecorateMemoryCacheWithMetrics("cache", configureOptions: options =>
{
    options.DisposeInner = false; // Don't dispose singleton
});
```

## Debugging Techniques

### 1. Enable Detailed Logging

```csharp
// Add detailed logging for dependency injection
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Log OpenTelemetry metrics export
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddConsoleExporter() // Add console exporter for debugging
        .AddOtlpExporter());
```

### 2. Verify Service Registration

```csharp
// Check service registrations at startup
public void ConfigureServices(IServiceCollection services)
{
    services.AddNamedMeteredMemoryCache("user-cache");

    // Debug service registrations
    var serviceProvider = services.BuildServiceProvider();
    using var scope = serviceProvider.CreateScope();

    try
    {
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var isMetered = cache is MeteredMemoryCache;
        Console.WriteLine($"Cache is metered: {isMetered}");

        var meter = scope.ServiceProvider.GetRequiredService<Meter>();
        Console.WriteLine($"Meter name: {meter.Name}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Service resolution failed: {ex.Message}");
    }
}
```

### 3. Validate Metrics Collection

```csharp
// Create a test harness to verify metrics
public class MetricsTestHarness
{
    private readonly List<Measurement<long>> _measurements = new();
    private readonly MeterProvider _meterProvider;

    public MetricsTestHarness()
    {
        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("MyApp.Cache")
            .AddInMemoryExporter(_measurements)
            .Build();
    }

    public void TestCacheMetrics()
    {
        using var serviceProvider = CreateServiceProvider();
        var cache = serviceProvider.GetRequiredService<IMemoryCache>();

        // Perform cache operations
        cache.Set("key1", "value1");
        cache.TryGetValue("key1", out _); // Hit
        cache.TryGetValue("key2", out _); // Miss

        // Verify metrics were collected
        _meterProvider.ForceFlush(TimeSpan.FromSeconds(1));

        var hits = _measurements.Where(m => m.Name == "cache_hits_total").Sum(m => m.Value);
        var misses = _measurements.Where(m => m.Name == "cache_misses_total").Sum(m => m.Value);

        Console.WriteLine($"Hits: {hits}, Misses: {misses}");
    }
}
```

### 4. Monitor Memory Usage

```csharp
// Track memory allocation in development
public class MemoryTrackingCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private long _allocationBytes;

    public MemoryTrackingCache(IMemoryCache inner)
    {
        _inner = inner;
    }

    public ICacheEntry CreateEntry(object key)
    {
        var before = GC.GetTotalMemory(false);
        var entry = _inner.CreateEntry(key);
        var after = GC.GetTotalMemory(false);

        Interlocked.Add(ref _allocationBytes, after - before);
        Console.WriteLine($"CreateEntry allocated: {after - before} bytes (Total: {_allocationBytes})");

        return entry;
    }

    // Implement other IMemoryCache methods...
}
```

## Diagnostic Commands

### Verify OpenTelemetry Integration

```bash
# Check if metrics are being exported (using OTLP HTTP exporter)
curl -X POST http://localhost:4318/v1/metrics \
  -H "Content-Type: application/x-protobuf" \
  -H "Accept: application/x-protobuf" \
  --data-binary @- <<< ""

# Expected: Should not return 404 if collector is running
```

### Performance Profiling

```csharp
// Use BenchmarkDotNet for performance analysis
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class CachePerformanceBenchmark
{
    private IMemoryCache _rawCache;
    private IMemoryCache _meteredCache;

    [GlobalSetup]
    public void Setup()
    {
        _rawCache = new MemoryCache(new MemoryCacheOptions());

        var meter = new Meter("Benchmark");
        _meteredCache = new MeteredMemoryCache(_rawCache, meter, "benchmark");
    }

    [Benchmark(Baseline = true)]
    public void RawCache_Operation() => _rawCache.Set("key", "value");

    [Benchmark]
    public void MeteredCache_Operation() => _meteredCache.Set("key", "value");
}
```

## Error Messages and Solutions

### "No IMemoryCache registration found"

**Error**: `InvalidOperationException: No IMemoryCache registration found. Register IMemoryCache before calling DecorateMemoryCacheWithMetrics.`

**Solution**: Register a base cache implementation first:

```csharp
services.AddMemoryCache(); // Add this first
services.DecorateMemoryCacheWithMetrics("my-cache");
```

### "Cache name must be non-empty"

**Error**: `ArgumentException: Cache name must be non-empty (Parameter 'cacheName')`

**Solution**: Provide a valid cache name:

```csharp
// ❌ services.AddNamedMeteredMemoryCache("");
services.AddNamedMeteredMemoryCache("valid-cache-name"); // ✅
```

### "Duplicate cache name registration"

**Error**: Multiple caches registered with the same name causing conflicts.

**Solution**: Use unique cache names or keyed services:

```csharp
services.AddNamedMeteredMemoryCache("user-cache");
services.AddNamedMeteredMemoryCache("product-cache"); // Different name
```

### "MeterProvider is disposed"

**Error**: Metrics stop working after application shutdown or container restart.

**Solution**: Ensure proper disposal order:

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register MeterProvider as singleton, not scoped
        services.AddSingleton<MeterProvider>(sp =>
            Sdk.CreateMeterProviderBuilder()
                .AddMeter("MyApp.Cache")
                .Build());
    }
}
```

## Best Practices for Troubleshooting

### 1. Use Structured Logging

```csharp
services.AddLogging(builder =>
{
    builder.AddStructuredConsole(); // Or Serilog
    builder.AddFilter("MeteredMemoryCache", LogLevel.Debug);
});
```

### 2. Implement Health Checks

```csharp
services.AddHealthChecks()
    .AddCheck<CacheHealthCheck>("cache-metrics");

public class CacheHealthCheck : IHealthCheck
{
    private readonly IMemoryCache _cache;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Test cache operations
            _cache.Set("health-check", DateTime.UtcNow, TimeSpan.FromSeconds(1));
            var retrieved = _cache.TryGetValue("health-check", out _);

            return retrieved
                ? HealthCheckResult.Healthy("Cache is operational")
                : HealthCheckResult.Degraded("Cache operations failing");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cache is not responding", ex);
        }
    }
}
```

### 3. Monitor in Production

```csharp
// Add application insights or similar monitoring
services.AddApplicationInsightsTelemetry();

// Custom metrics for monitoring
services.AddSingleton<IHostedService, MetricsMonitoringService>();

public class MetricsMonitoringService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Check if metrics are flowing
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

## When to Seek Additional Help

Contact the development team or file an issue if you encounter:

1. **Unexpected exceptions** not covered in this guide
2. **Performance degradation** exceeding documented overhead (>50ns for reads, >100ns for writes)
3. **Memory leaks** related to metric collection
4. **Thread safety issues** in high-concurrency scenarios
5. **Integration problems** with specific OpenTelemetry exporters

## Related Documentation

- [MeteredMemoryCache Usage Guide](./MeteredMemoryCache.md)
- [OpenTelemetry Integration](./OpenTelemetryIntegration.md)
- [Performance Characteristics](./PerformanceCharacteristics.md)
- [Multi-Cache Scenarios](./MultiCacheScenarios.md)

For additional support, see the project's GitHub issues or discussions section.
