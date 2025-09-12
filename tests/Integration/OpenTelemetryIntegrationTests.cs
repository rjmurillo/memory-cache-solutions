using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Integration;

/// <summary>
/// Integration tests for MeteredMemoryCache OpenTelemetry metrics collection and validation.
/// Tests the complete end-to-end flow from cache operations to metric emission.
/// </summary>
public class OpenTelemetryIntegrationTests
{
    /// <summary>
    /// Tests that MeteredMemoryCache correctly emits hit metrics to OpenTelemetry.
    /// </summary>
    [Fact]
    public async Task MeteredMemoryCache_Hit_EmitsMetricsToOpenTelemetry()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithMetrics(exportedItems);
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<IMemoryCache>();
        
        // Pre-populate cache
        cache.Set("test-key", "test-value");

        // Act - perform cache hit
        var result = cache.TryGetValue("test-key", out var value);

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        Assert.True(result);
        Assert.Equal("test-value", value);
        
        var hitMetric = FindMetric(exportedItems, "cache_hits_total");
        Assert.NotNull(hitMetric);
        AssertMetricValue(hitMetric, 1);
    }

    /// <summary>
    /// Tests that MeteredMemoryCache correctly emits miss metrics to OpenTelemetry.
    /// </summary>
    [Fact]
    public async Task MeteredMemoryCache_Miss_EmitsMetricsToOpenTelemetry()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithMetrics(exportedItems);
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<IMemoryCache>();

        // Act - perform cache miss
        var result = cache.TryGetValue("non-existent-key", out var value);

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        Assert.False(result);
        Assert.Null(value);
        
        var missMetric = FindMetric(exportedItems, "cache_misses_total");
        Assert.NotNull(missMetric);
        AssertMetricValue(missMetric, 1);
    }

    /// <summary>
    /// Tests that MeteredMemoryCache correctly emits eviction metrics to OpenTelemetry.
    /// </summary>
    [Fact]
    public async Task MeteredMemoryCache_Eviction_EmitsMetricsToOpenTelemetry()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithMetrics(exportedItems, cacheOptions: opt => 
        {
            opt.SizeLimit = 1; // Force eviction after one item
        });
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<IMemoryCache>();

        // Act - add items to force eviction
        cache.Set("key1", "value1", new MemoryCacheEntryOptions { Size = 1 });
        cache.Set("key2", "value2", new MemoryCacheEntryOptions { Size = 1 }); // Should evict key1

        // Give time for eviction callback to execute
        await Task.Delay(100);

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var evictionMetric = FindMetric(exportedItems, "cache_evictions_total");
        Assert.NotNull(evictionMetric);
        AssertMetricValue(evictionMetric, 1);
    }

    /// <summary>
    /// Tests that named caches emit metrics with correct cache.name tags.
    /// </summary>
    [Fact]
    public async Task NamedCache_EmitsMetricsWithCorrectTags()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithNamedCache(exportedItems, "user-cache");
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<IMemoryCache>();

        // Act
        cache.Set("test-key", "test-value");
        cache.TryGetValue("test-key", out _); // Hit
        cache.TryGetValue("missing-key", out _); // Miss

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var hitMetric = FindMetric(exportedItems, "cache_hits_total");
        Assert.NotNull(hitMetric);
        AssertMetricHasTag(hitMetric, "cache.name", "user-cache");
        
        var missMetric = FindMetric(exportedItems, "cache_misses_total");
        Assert.NotNull(missMetric);
        AssertMetricHasTag(missMetric, "cache.name", "user-cache");
    }

    /// <summary>
    /// Tests that multiple named caches emit separate metrics.
    /// </summary>
    [Fact]
    public async Task MultipleCaches_EmitSeparateMetrics()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithMultipleNamedCaches(exportedItems);
        await host.StartAsync();

        var serviceProvider = host.Services;
        var userCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("user-cache");
        var productCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("product-cache");

        // Act
        userCache.Set("user1", "data1");
        userCache.TryGetValue("user1", out _); // Hit for user-cache
        
        productCache.Set("product1", "data1");
        productCache.TryGetValue("missing", out _); // Miss for product-cache

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var hitMetrics = FindMetrics(exportedItems, "cache_hits_total");
        var userCacheHits = hitMetrics.Where(m => HasTag(m, "cache.name", "user-cache"));
        var productCacheHits = hitMetrics.Where(m => HasTag(m, "cache.name", "product-cache"));
        
        Assert.Single(userCacheHits);
        Assert.Empty(productCacheHits); // No hits for product cache
        
        var missMetrics = FindMetrics(exportedItems, "cache_misses_total");
        var productCacheMisses = missMetrics.Where(m => HasTag(m, "cache.name", "product-cache"));
        
        Assert.Single(productCacheMisses);
    }

    /// <summary>
    /// Tests that additional tags are properly emitted with metrics.
    /// </summary>
    [Fact]
    public async Task CacheWithAdditionalTags_EmitsAllTags()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithAdditionalTags(exportedItems);
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<IMemoryCache>();

        // Act
        cache.TryGetValue("missing-key", out _); // Miss

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var missMetric = FindMetric(exportedItems, "cache_misses_total");
        Assert.NotNull(missMetric);
        AssertMetricHasTag(missMetric, "cache.name", "tagged-cache");
        AssertMetricHasTag(missMetric, "environment", "test");
        AssertMetricHasTag(missMetric, "region", "us-west-2");
    }

    /// <summary>
    /// Tests concurrent cache operations to ensure thread-safe metric emission.
    /// </summary>
    [Fact]
    public async Task ConcurrentOperations_EmitCorrectMetrics()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithMetrics(exportedItems);
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<IMemoryCache>();
        const int operationsPerType = 50;

        // Pre-populate some keys for hits
        for (int i = 0; i < operationsPerType / 2; i++)
        {
            cache.Set($"key-{i}", $"value-{i}");
        }

        // Act - perform concurrent operations
        var tasks = new List<Task>();

        // Add hit operations
        for (int i = 0; i < operationsPerType / 2; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() => cache.TryGetValue($"key-{index}", out _)));
        }

        // Add miss operations  
        for (int i = 0; i < operationsPerType / 2; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() => cache.TryGetValue($"missing-{index}", out _)));
        }

        await Task.WhenAll(tasks);

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var hitMetric = FindMetric(exportedItems, "cache_hits_total");
        var missMetric = FindMetric(exportedItems, "cache_misses_total");
        
        Assert.NotNull(hitMetric);
        Assert.NotNull(missMetric);
        
        AssertMetricValue(hitMetric, operationsPerType / 2);
        AssertMetricValue(missMetric, operationsPerType / 2);
    }

    #region Helper Methods

    private static IHost CreateHostWithMetrics(
        List<Metric> exportedItems, 
        Action<MemoryCacheOptions>? cacheOptions = null)
    {
        var builder = new HostApplicationBuilder();
        
        // Add memory cache
        if (cacheOptions != null)
        {
            builder.Services.AddMemoryCache(cacheOptions);
        }
        else
        {
            builder.Services.AddMemoryCache();
        }

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Decorate the cache with metrics
        builder.Services.DecorateMemoryCacheWithMetrics("test-cache");

        return builder.Build();
    }

    private static IHost CreateHostWithNamedCache(List<Metric> exportedItems, string cacheName)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Add named cache with metrics
        builder.Services.AddNamedMeteredMemoryCache(cacheName);

        return builder.Build();
    }

    private static IHost CreateHostWithMultipleNamedCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Add multiple named caches
        builder.Services.AddNamedMeteredMemoryCache("user-cache");
        builder.Services.AddNamedMeteredMemoryCache("product-cache");

        return builder.Build();
    }

    private static IHost CreateHostWithAdditionalTags(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Add cache with additional tags
        builder.Services.AddNamedMeteredMemoryCache("tagged-cache", configureOptions: opt =>
        {
            opt.AdditionalTags["environment"] = "test";
            opt.AdditionalTags["region"] = "us-west-2";
        });

        return builder.Build();
    }

    private static async Task FlushMetricsAsync(IHost host)
    {
        // Force metrics collection by getting the MeterProvider and calling ForceFlush
        var meterProvider = host.Services.GetService<MeterProvider>();
        if (meterProvider != null)
        {
            meterProvider.ForceFlush(5000); // 5000ms = 5 seconds
        }
        
        // Give additional time for async operations
        await Task.Delay(50);
    }

    private static Metric? FindMetric(List<Metric> metrics, string name)
    {
        return metrics.FirstOrDefault(m => m.Name == name);
    }

    private static IEnumerable<Metric> FindMetrics(List<Metric> metrics, string name)
    {
        return metrics.Where(m => m.Name == name);
    }

    private static void AssertMetricValue(Metric metric, long expectedValue)
    {
        var metricPoints = new List<MetricPoint>();
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            metricPoints.Add(mp);
        }

        var totalValue = metricPoints.Sum(mp => mp.GetSumLong());
        Assert.Equal(expectedValue, totalValue);
    }

    private static void AssertMetricHasTag(Metric metric, string tagKey, string expectedValue)
    {
        var hasTag = false;
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == tagKey && tag.Value?.ToString() == expectedValue)
                {
                    hasTag = true;
                    break;
                }
            }
            if (hasTag) break;
        }
        
        Assert.True(hasTag, $"Metric should have tag '{tagKey}' with value '{expectedValue}'");
    }

    private static bool HasTag(Metric metric, string tagKey, string expectedValue)
    {
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == tagKey && tag.Value?.ToString() == expectedValue)
                {
                    return true;
                }
            }
        }
        return false;
    }

    #endregion
}
