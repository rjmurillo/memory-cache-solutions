using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Integration;

/// <summary>
/// Integration tests for multi-cache scenarios with different names and tags.
/// Tests complex scenarios involving multiple named caches operating simultaneously
/// with different configurations and ensuring proper metric isolation.
/// </summary>
public class MultiCacheScenarioTests
{
    /// <summary>
    /// Tests that three different named caches can operate independently
    /// with separate metric emission and no cross-contamination.
    /// </summary>
    [Fact]
    public async Task ThreeNamedCaches_OperateIndependentlyWithSeparateMetrics()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithThreeNamedCaches(exportedItems);
        await host.StartAsync();

        var serviceProvider = host.Services;
        var userCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("user-cache");
        var productCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("product-cache");
        var sessionCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("session-cache");

        // Act - Perform different operations on each cache
        // User cache: 2 hits, 1 miss
        userCache.Set("user1", new { Name = "Alice", Id = 1 });
        userCache.Set("user2", new { Name = "Bob", Id = 2 });
        userCache.TryGetValue("user1", out _); // Hit
        userCache.TryGetValue("user2", out _); // Hit
        userCache.TryGetValue("user999", out _); // Miss

        // Product cache: 1 hit, 2 misses
        productCache.Set("product1", new { Name = "Widget", Price = 10.99 });
        productCache.TryGetValue("product1", out _); // Hit
        productCache.TryGetValue("product999", out _); // Miss
        productCache.TryGetValue("product888", out _); // Miss

        // Session cache: 3 hits, 0 misses
        sessionCache.Set("session1", "session-data-1");
        sessionCache.Set("session2", "session-data-2");
        sessionCache.Set("session3", "session-data-3");
        sessionCache.TryGetValue("session1", out _); // Hit
        sessionCache.TryGetValue("session2", out _); // Hit
        sessionCache.TryGetValue("session3", out _); // Hit

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert - Verify each cache has correct metrics
        var hitMetrics = FindMetrics(exportedItems, "cache_hits_total");
        var missMetrics = FindMetrics(exportedItems, "cache_misses_total");

        // User cache assertions
        var userHits = hitMetrics.Where(m => HasTag(m, "cache.name", "user-cache"));
        var userMisses = missMetrics.Where(m => HasTag(m, "cache.name", "user-cache"));
        Assert.Single(userHits);
        Assert.Single(userMisses);
        AssertMetricValue(userHits.First(), 2);
        AssertMetricValue(userMisses.First(), 1);

        // Product cache assertions
        var productHits = hitMetrics.Where(m => HasTag(m, "cache.name", "product-cache"));
        var productMisses = missMetrics.Where(m => HasTag(m, "cache.name", "product-cache"));
        Assert.Single(productHits);
        Assert.Single(productMisses);
        AssertMetricValue(productHits.First(), 1);
        AssertMetricValue(productMisses.First(), 2);

        // Session cache assertions
        var sessionHits = hitMetrics.Where(m => HasTag(m, "cache.name", "session-cache"));
        var sessionMisses = missMetrics.Where(m => HasTag(m, "cache.name", "session-cache"));
        Assert.Single(sessionHits);
        Assert.Empty(sessionMisses); // No misses for session cache
        AssertMetricValue(sessionHits.First(), 3);
    }

    /// <summary>
    /// Tests that caches with additional custom tags emit all configured tags correctly
    /// while maintaining isolation between different cache configurations.
    /// </summary>
    [Fact]
    public async Task CachesWithDifferentAdditionalTags_EmitDistinctMetrics()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithTaggedCaches(exportedItems);
        await host.StartAsync();

        var serviceProvider = host.Services;
        var prodCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("prod-cache");
        var stagingCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("staging-cache");

        // Act
        prodCache.Set("data1", "production-data");
        prodCache.TryGetValue("data1", out _); // Hit
        prodCache.TryGetValue("missing1", out _); // Miss

        stagingCache.Set("data2", "staging-data");
        stagingCache.TryGetValue("data2", out _); // Hit
        stagingCache.TryGetValue("missing2", out _); // Miss

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var hitMetrics = FindMetrics(exportedItems, "cache_hits_total");
        var missMetrics = FindMetrics(exportedItems, "cache_misses_total");

        // Production cache metrics
        var prodHits = hitMetrics.Where(m => HasTag(m, "cache.name", "prod-cache"));
        var prodMisses = missMetrics.Where(m => HasTag(m, "cache.name", "prod-cache"));
        Assert.Single(prodHits);
        Assert.Single(prodMisses);
        
        // Verify production cache has correct tags
        AssertMetricHasTag(prodHits.First(), "cache.name", "prod-cache");
        AssertMetricHasTag(prodHits.First(), "environment", "production");
        AssertMetricHasTag(prodHits.First(), "region", "us-east-1");
        AssertMetricHasTag(prodHits.First(), "tier", "premium");

        // Staging cache metrics
        var stagingHits = hitMetrics.Where(m => HasTag(m, "cache.name", "staging-cache"));
        var stagingMisses = missMetrics.Where(m => HasTag(m, "cache.name", "staging-cache"));
        Assert.Single(stagingHits);
        Assert.Single(stagingMisses);
        
        // Verify staging cache has correct tags
        AssertMetricHasTag(stagingHits.First(), "cache.name", "staging-cache");
        AssertMetricHasTag(stagingHits.First(), "environment", "staging");
        AssertMetricHasTag(stagingHits.First(), "region", "us-west-2");
        AssertMetricHasTag(stagingHits.First(), "tier", "standard");
    }

    /// <summary>
    /// Tests eviction scenarios across multiple caches to ensure
    /// eviction metrics are properly attributed to the correct cache.
    /// </summary>
    [Fact]
    public async Task MultiCacheEvictionScenarios_EmitCorrectEvictionMetrics()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithEvictionCaches(exportedItems);
        await host.StartAsync();

        var serviceProvider = host.Services;
        var smallCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("small-cache");
        var mediumCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("medium-cache");

        // Act - Force evictions in small cache (limit=1)
        smallCache.Set("item1", "data1", new MemoryCacheEntryOptions { Size = 1 });
        smallCache.Set("item2", "data2", new MemoryCacheEntryOptions { Size = 1 }); // Should evict item1
        smallCache.Set("item3", "data3", new MemoryCacheEntryOptions { Size = 1 }); // Should evict item2

        // Act - Force evictions in medium cache (limit=2)
        mediumCache.Set("med1", "data1", new MemoryCacheEntryOptions { Size = 1 });
        mediumCache.Set("med2", "data2", new MemoryCacheEntryOptions { Size = 1 });
        mediumCache.Set("med3", "data3", new MemoryCacheEntryOptions { Size = 1 }); // Should evict med1

        // Give time for eviction callbacks to execute
        await Task.Delay(200);

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var evictionMetrics = FindMetrics(exportedItems, "cache_evictions_total");

        var smallCacheEvictions = evictionMetrics.Where(m => HasTag(m, "cache.name", "small-cache"));
        var mediumCacheEvictions = evictionMetrics.Where(m => HasTag(m, "cache.name", "medium-cache"));

        // Small cache should have 2 evictions
        Assert.Single(smallCacheEvictions);
        AssertMetricValue(smallCacheEvictions.First(), 2);

        // Medium cache should have 1 eviction
        Assert.Single(mediumCacheEvictions);
        AssertMetricValue(mediumCacheEvictions.First(), 1);

        // Verify correct tags are present
        AssertMetricHasTag(smallCacheEvictions.First(), "cache.name", "small-cache");
        AssertMetricHasTag(smallCacheEvictions.First(), "purpose", "testing");
        AssertMetricHasTag(mediumCacheEvictions.First(), "cache.name", "medium-cache");
        AssertMetricHasTag(mediumCacheEvictions.First(), "purpose", "testing");
    }

    /// <summary>
    /// Tests concurrent operations across multiple named caches to ensure
    /// thread-safety and metric accuracy under load.
    /// </summary>
    [Fact]
    public async Task ConcurrentMultiCacheOperations_MaintainMetricAccuracy()
    {
        // Arrange
        var exportedItems = new List<Metric>();
        using var host = CreateHostWithConcurrencyCaches(exportedItems);
        await host.StartAsync();

        var serviceProvider = host.Services;
        var cache1 = serviceProvider.GetRequiredKeyedService<IMemoryCache>("concurrent-cache-1");
        var cache2 = serviceProvider.GetRequiredKeyedService<IMemoryCache>("concurrent-cache-2");

        const int operationsPerCache = 50;
        
        // Pre-populate caches for hits
        for (int i = 0; i < operationsPerCache / 2; i++)
        {
            cache1.Set($"cache1-key-{i}", $"cache1-value-{i}");
            cache2.Set($"cache2-key-{i}", $"cache2-value-{i}");
        }

        // Act - Perform concurrent operations
        var tasks = new List<Task>();

        // Cache 1 operations: 25 hits, 25 misses
        for (int i = 0; i < operationsPerCache; i++)
        {
            int index = i;
            if (index < operationsPerCache / 2)
            {
                // Hit operation
                tasks.Add(Task.Run(() => cache1.TryGetValue($"cache1-key-{index}", out _)));
            }
            else
            {
                // Miss operation
                tasks.Add(Task.Run(() => cache1.TryGetValue($"cache1-missing-{index}", out _)));
            }
        }

        // Cache 2 operations: 25 hits, 25 misses
        for (int i = 0; i < operationsPerCache; i++)
        {
            int index = i;
            if (index < operationsPerCache / 2)
            {
                // Hit operation
                tasks.Add(Task.Run(() => cache2.TryGetValue($"cache2-key-{index}", out _)));
            }
            else
            {
                // Miss operation
                tasks.Add(Task.Run(() => cache2.TryGetValue($"cache2-missing-{index}", out _)));
            }
        }

        await Task.WhenAll(tasks);

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert
        var hitMetrics = FindMetrics(exportedItems, "cache_hits_total");
        var missMetrics = FindMetrics(exportedItems, "cache_misses_total");

        // Cache 1 assertions
        var cache1Hits = hitMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-1"));
        var cache1Misses = missMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-1"));
        Assert.Single(cache1Hits);
        Assert.Single(cache1Misses);
        AssertMetricValue(cache1Hits.First(), operationsPerCache / 2);
        AssertMetricValue(cache1Misses.First(), operationsPerCache / 2);

        // Cache 2 assertions
        var cache2Hits = hitMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-2"));
        var cache2Misses = missMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-2"));
        Assert.Single(cache2Hits);
        Assert.Single(cache2Misses);
        AssertMetricValue(cache2Hits.First(), operationsPerCache / 2);
        AssertMetricValue(cache2Misses.First(), operationsPerCache / 2);
    }

    /// <summary>
    /// Tests that caches with different meter names are properly isolated
    /// and metrics are attributed to the correct meter.
    /// </summary>
    [Fact]
    public async Task CachesWithDifferentMeterNames_EmitToCorrectMeters()
    {
        // Arrange
        var mainMeterItems = new List<Metric>();
        var secondaryMeterItems = new List<Metric>();
        using var host = CreateHostWithDifferentMeters(mainMeterItems, secondaryMeterItems);
        await host.StartAsync();

        var serviceProvider = host.Services;
        var mainCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("main-cache");
        var secondaryCache = serviceProvider.GetRequiredKeyedService<IMemoryCache>("secondary-cache");

        // Act
        mainCache.Set("main-data", "main-value");
        mainCache.TryGetValue("main-data", out _); // Hit
        
        secondaryCache.Set("secondary-data", "secondary-value");
        secondaryCache.TryGetValue("secondary-data", out _); // Hit

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Assert - Main meter should have metrics from main-cache
        var mainHitMetrics = FindMetrics(mainMeterItems, "cache_hits_total");
        Assert.Single(mainHitMetrics);
        AssertMetricHasTag(mainHitMetrics.First(), "cache.name", "main-cache");

        // Secondary meter should have metrics from secondary-cache
        var secondaryHitMetrics = FindMetrics(secondaryMeterItems, "cache_hits_total");
        Assert.Single(secondaryHitMetrics);
        AssertMetricHasTag(secondaryHitMetrics.First(), "cache.name", "secondary-cache");

        // Verify no cross-contamination
        Assert.DoesNotContain(mainMeterItems, m => m.Name.Contains("secondary"));
        Assert.DoesNotContain(secondaryMeterItems, m => m.Name.Contains("main"));
    }

    #region Helper Methods

    private static IHost CreateHostWithThreeNamedCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Add three named caches
        builder.Services.AddNamedMeteredMemoryCache("user-cache");
        builder.Services.AddNamedMeteredMemoryCache("product-cache");
        builder.Services.AddNamedMeteredMemoryCache("session-cache");

        return builder.Build();
    }

    private static IHost CreateHostWithTaggedCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Add production cache with production tags
        builder.Services.AddNamedMeteredMemoryCache("prod-cache", configureOptions: opt =>
        {
            opt.AdditionalTags["environment"] = "production";
            opt.AdditionalTags["region"] = "us-east-1";
            opt.AdditionalTags["tier"] = "premium";
        });

        // Add staging cache with staging tags
        builder.Services.AddNamedMeteredMemoryCache("staging-cache", configureOptions: opt =>
        {
            opt.AdditionalTags["environment"] = "staging";
            opt.AdditionalTags["region"] = "us-west-2";
            opt.AdditionalTags["tier"] = "standard";
        });

        return builder.Build();
    }

    private static IHost CreateHostWithEvictionCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Configure memory cache options with size limits
        builder.Services.Configure<MemoryCacheOptions>("small-cache-options", opt =>
        {
            opt.SizeLimit = 1;
        });

        builder.Services.Configure<MemoryCacheOptions>("medium-cache-options", opt =>
        {
            opt.SizeLimit = 2;
        });

        // Add small cache (size limit = 1)
        builder.Services.AddKeyedSingleton<IMemoryCache>("small-cache", (sp, key) =>
        {
            var innerCache = new MemoryCache(sp.GetRequiredService<IOptionsMonitor<MemoryCacheOptions>>()
                .Get("small-cache-options"));
            var meter = sp.GetRequiredService<Meter>();
            var options = new MeteredMemoryCacheOptions
            {
                CacheName = "small-cache",
                AdditionalTags = { ["purpose"] = "testing", ["size"] = "small" }
            };
            return new MeteredMemoryCache(innerCache, meter, options);
        });

        // Add medium cache (size limit = 2)
        builder.Services.AddKeyedSingleton<IMemoryCache>("medium-cache", (sp, key) =>
        {
            var innerCache = new MemoryCache(sp.GetRequiredService<IOptionsMonitor<MemoryCacheOptions>>()
                .Get("medium-cache-options"));
            var meter = sp.GetRequiredService<Meter>();
            var options = new MeteredMemoryCacheOptions
            {
                CacheName = "medium-cache",
                AdditionalTags = { ["purpose"] = "testing", ["size"] = "medium" }
            };
            return new MeteredMemoryCache(innerCache, meter, options);
        });

        // Register meter
        builder.Services.TryAddSingleton<Meter>(sp => new Meter("MeteredMemoryCache"));

        return builder.Build();
    }

    private static IHost CreateHostWithConcurrencyCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MeteredMemoryCache")
                .AddInMemoryExporter(exportedItems));

        // Add two caches for concurrency testing
        builder.Services.AddNamedMeteredMemoryCache("concurrent-cache-1", configureOptions: opt =>
        {
            opt.AdditionalTags["test-type"] = "concurrency";
            opt.AdditionalTags["cache-id"] = "1";
        });

        builder.Services.AddNamedMeteredMemoryCache("concurrent-cache-2", configureOptions: opt =>
        {
            opt.AdditionalTags["test-type"] = "concurrency";
            opt.AdditionalTags["cache-id"] = "2";
        });

        return builder.Build();
    }

    private static IHost CreateHostWithDifferentMeters(List<Metric> mainMeterItems, List<Metric> secondaryMeterItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with InMemory exporters for different meters
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("MainMeter")
                .AddInMemoryExporter(mainMeterItems)
                .AddMeter("SecondaryMeter")
                .AddInMemoryExporter(secondaryMeterItems));

        // Add cache with main meter
        builder.Services.AddNamedMeteredMemoryCache("main-cache", meterName: "MainMeter");

        // Add cache with secondary meter
        builder.Services.AddNamedMeteredMemoryCache("secondary-cache", meterName: "SecondaryMeter");

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
