using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using OpenTelemetry.Metrics;

using Unit;

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

        // Wait for expected metrics using deterministic timing with cache-specific filtering
        var userHitsDetected = await WaitForMetricValueWithCacheFilterAsync(exportedItems, "cache_hits_total", "user-cache", 2, TimeSpan.FromSeconds(5));
        var userMissesDetected = await WaitForMetricValueWithCacheFilterAsync(exportedItems, "cache_misses_total", "user-cache", 1, TimeSpan.FromSeconds(5));
        var productHitsDetected = await WaitForMetricValueWithCacheFilterAsync(exportedItems, "cache_hits_total", "product-cache", 1, TimeSpan.FromSeconds(5));
        var productMissesDetected = await WaitForMetricValueWithCacheFilterAsync(exportedItems, "cache_misses_total", "product-cache", 2, TimeSpan.FromSeconds(5));
        var sessionHitsDetected = await WaitForMetricValueWithCacheFilterAsync(exportedItems, "cache_hits_total", "session-cache", 3, TimeSpan.FromSeconds(5));

        // Assert - Verify each cache has correct metrics
        Assert.True(userHitsDetected, "User cache hit metrics should be detected within timeout");
        Assert.True(userMissesDetected, "User cache miss metrics should be detected within timeout");
        Assert.True(productHitsDetected, "Product cache hit metrics should be detected within timeout");
        Assert.True(productMissesDetected, "Product cache miss metrics should be detected within timeout");
        Assert.True(sessionHitsDetected, "Session cache hit metrics should be detected within timeout");

        var hitMetrics = FindMetrics(exportedItems, "cache_hits_total");
        var missMetrics = FindMetrics(exportedItems, "cache_misses_total");

        // User cache assertions
        var userHits = hitMetrics.Where(m => HasTag(m, "cache.name", "user-cache"));
        var userMisses = missMetrics.Where(m => HasTag(m, "cache.name", "user-cache"));
        Assert.Single(userHits);
        Assert.Single(userMisses);
        AssertMetricValueForCache(userHits.First(), "user-cache", 2);
        AssertMetricValueForCache(userMisses.First(), "user-cache", 1);

        // Product cache assertions
        var productHits = hitMetrics.Where(m => HasTag(m, "cache.name", "product-cache"));
        var productMisses = missMetrics.Where(m => HasTag(m, "cache.name", "product-cache"));
        Assert.Single(productHits);
        Assert.Single(productMisses);
        AssertMetricValueForCache(productHits.First(), "product-cache", 1);
        AssertMetricValueForCache(productMisses.First(), "product-cache", 2);

        // Session cache assertions
        var sessionHits = hitMetrics.Where(m => HasTag(m, "cache.name", "session-cache"));
        var sessionMisses = missMetrics.Where(m => HasTag(m, "cache.name", "session-cache"));
        Assert.Single(sessionHits);
        Assert.Empty(sessionMisses); // No misses for session cache
        AssertMetricValueForCache(sessionHits.First(), "session-cache", 3);
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

        // Act - Force evictions using CancellationChangeToken for reliable evictions
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var cts3 = new CancellationTokenSource();
        var cts4 = new CancellationTokenSource();
        var cts5 = new CancellationTokenSource();
        var cts6 = new CancellationTokenSource();

        smallCache.Set("item1", "data1", new MemoryCacheEntryOptions { Size = 1, ExpirationTokens = { new CancellationChangeToken(cts1.Token) } });
        smallCache.Set("item2", "data2", new MemoryCacheEntryOptions { Size = 1, ExpirationTokens = { new CancellationChangeToken(cts2.Token) } });
        smallCache.Set("item3", "data3", new MemoryCacheEntryOptions { Size = 1, ExpirationTokens = { new CancellationChangeToken(cts3.Token) } });

        mediumCache.Set("med1", "data1", new MemoryCacheEntryOptions { Size = 1, ExpirationTokens = { new CancellationChangeToken(cts4.Token) } });
        mediumCache.Set("med2", "data2", new MemoryCacheEntryOptions { Size = 1, ExpirationTokens = { new CancellationChangeToken(cts5.Token) } });
        mediumCache.Set("med3", "data3", new MemoryCacheEntryOptions { Size = 1, ExpirationTokens = { new CancellationChangeToken(cts6.Token) } });

        // Trigger evictions by canceling tokens
        cts1.Cancel(); // Evict item1 from small cache
        cts2.Cancel(); // Evict item2 from small cache
        cts4.Cancel(); // Evict med1 from medium cache

        // Force metrics collection
        await FlushMetricsAsync(host);

        // Wait for expected eviction metrics using deterministic timing
        var smallCacheEvictionsDetected = await WaitForMetricValueAsync(exportedItems, "cache_evictions_total", 2, TimeSpan.FromSeconds(5));
        var mediumCacheEvictionsDetected = await WaitForMetricValueAsync(exportedItems, "cache_evictions_total", 1, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(smallCacheEvictionsDetected, "Small cache evictions should be detected within timeout");
        Assert.True(mediumCacheEvictionsDetected, "Medium cache evictions should be detected within timeout");

        var evictionMetrics = FindMetrics(exportedItems, "cache_evictions_total");

        var smallCacheEvictions = evictionMetrics.Where(m => HasTag(m, "cache.name", "small-cache"));
        var mediumCacheEvictions = evictionMetrics.Where(m => HasTag(m, "cache.name", "medium-cache"));

        // Small cache should have 2 evictions
        Assert.Single(smallCacheEvictions);
        AssertMetricValueForCache(smallCacheEvictions.First(), "small-cache", 2);

        // Medium cache should have 1 eviction
        Assert.Single(mediumCacheEvictions);
        AssertMetricValueForCache(mediumCacheEvictions.First(), "medium-cache", 1);

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

        // Wait for expected metrics using deterministic timing
        var hitsDetected = await WaitForMetricValueAsync(exportedItems, "cache_hits_total", operationsPerCache, TimeSpan.FromSeconds(5));
        var missesDetected = await WaitForMetricValueAsync(exportedItems, "cache_misses_total", operationsPerCache, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(hitsDetected, "Expected hit metrics should be detected within timeout");
        Assert.True(missesDetected, "Expected miss metrics should be detected within timeout");

        var hitMetrics = FindMetrics(exportedItems, "cache_hits_total");
        var missMetrics = FindMetrics(exportedItems, "cache_misses_total");

        // Cache 1 assertions
        var cache1Hits = hitMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-1"));
        var cache1Misses = missMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-1"));
        Assert.Single(cache1Hits);
        Assert.Single(cache1Misses);
        AssertMetricValueForCache(cache1Hits.First(), "concurrent-cache-1", operationsPerCache / 2);
        AssertMetricValueForCache(cache1Misses.First(), "concurrent-cache-1", operationsPerCache / 2);

        // Cache 2 assertions
        var cache2Hits = hitMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-2"));
        var cache2Misses = missMetrics.Where(m => HasTag(m, "cache.name", "concurrent-cache-2"));
        Assert.Single(cache2Hits);
        Assert.Single(cache2Misses);
        AssertMetricValueForCache(cache2Hits.First(), "concurrent-cache-2", operationsPerCache / 2);
        AssertMetricValueForCache(cache2Misses.First(), "concurrent-cache-2", operationsPerCache / 2);
    }

    /// <summary>
    /// Tests that caches with different meter names are properly isolated
    /// and metrics are attributed to the correct meter.
    /// </summary>
    [Fact]
    public async Task CachesWithDifferentMeterNames_EmitToCorrectMeters()
    {
        // Arrange - Create separate hosts for complete isolation
        var mainMeterItems = new List<Metric>();
        var secondaryMeterItems = new List<Metric>();

        using var mainHost = CreateHostWithSingleMeter("MainMeter", mainMeterItems);
        using var secondaryHost = CreateHostWithSingleMeter("SecondaryMeter", secondaryMeterItems);

        await mainHost.StartAsync();
        await secondaryHost.StartAsync();

        var mainCache = mainHost.Services.GetRequiredKeyedService<IMemoryCache>("main-cache");
        var secondaryCache = secondaryHost.Services.GetRequiredKeyedService<IMemoryCache>("secondary-cache");

        // Act
        mainCache.Set("main-data", "main-value");
        mainCache.TryGetValue("main-data", out _); // Hit

        secondaryCache.Set("secondary-data", "secondary-value");
        secondaryCache.TryGetValue("secondary-data", out _); // Hit

        // Force metrics collection
        await FlushMetricsAsync(mainHost);
        await FlushMetricsAsync(secondaryHost);

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

    // Helper Methods

    /// <summary>
    /// Deterministic wait helper that polls for expected metric count instead of using Task.Delay.
    /// Eliminates sleep semantics and provides reliable test execution.
    /// </summary>
    private static async Task<bool> WaitForMetricCountAsync(List<Metric> exportedItems, string metricName, int expectedCount, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var currentCount = exportedItems.Count(m => m.Name == metricName);
            if (currentCount >= expectedCount)
            {
                return true;
            }
            await Task.Yield(); // Yield control without blocking
        }
        return false;
    }

    /// <summary>
    /// Deterministic wait helper that polls for expected metric value instead of using Task.Delay.
    /// Eliminates sleep semantics and provides reliable test execution.
    /// </summary>
    private static async Task<bool> WaitForMetricValueAsync(List<Metric> exportedItems, string metricName, long expectedValue, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var metrics = exportedItems.Where(m => m.Name == metricName);
            foreach (var metric in metrics)
            {
                var metricPoints = new List<MetricPoint>();
                foreach (ref readonly var mp in metric.GetMetricPoints())
                {
                    metricPoints.Add(mp);
                }
                var totalValue = metricPoints.Sum(mp => mp.GetSumLong());
                if (totalValue >= expectedValue)
                {
                    return true;
                }
            }
            await Task.Yield(); // Yield control without blocking
        }
        return false;
    }

    /// <summary>
    /// Deterministic wait helper that polls for expected metric value with specific cache name filter.
    /// Eliminates sleep semantics and provides reliable test execution.
    /// </summary>
    private static async Task<bool> WaitForMetricValueWithCacheFilterAsync(List<Metric> exportedItems, string metricName, string cacheName, long expectedValue, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var metrics = exportedItems.Where(m => m.Name == metricName);
            foreach (var metric in metrics)
            {
                var metricPoints = new List<MetricPoint>();
                foreach (ref readonly var mp in metric.GetMetricPoints())
                {
                    metricPoints.Add(mp);
                }

                // Filter by cache name
                var filteredValue = 0L;
                foreach (var mp in metricPoints)
                {
                    var hasCacheTag = false;
                    foreach (var tag in mp.Tags)
                    {
                        if (tag.Key == "cache.name" && tag.Value?.ToString() == cacheName)
                        {
                            hasCacheTag = true;
                            break;
                        }
                    }
                    if (hasCacheTag)
                    {
                        filteredValue += mp.GetSumLong();
                    }
                }

                if (filteredValue >= expectedValue)
                {
                    return true;
                }
            }
            await Task.Yield(); // Yield control without blocking
        }
        return false;
    }

    private static IHost CreateHostWithThreeNamedCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();
        var meterName = SharedUtilities.GetUniqueMeterName("test.three.caches");

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(meterName)
                .AddInMemoryExporter(exportedItems));

        // Add three named caches
        builder.Services.AddNamedMeteredMemoryCache("user-cache", meterName: meterName);
        builder.Services.AddNamedMeteredMemoryCache("product-cache", meterName: meterName);
        builder.Services.AddNamedMeteredMemoryCache("session-cache", meterName: meterName);

        return builder.Build();
    }

    private static IHost CreateHostWithTaggedCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();
        var meterName = SharedUtilities.GetUniqueMeterName("test.tagged.caches");

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(meterName)
                .AddInMemoryExporter(exportedItems));

        // Add production cache with production tags
        builder.Services.AddNamedMeteredMemoryCache("prod-cache", meterName: meterName, configureOptions: opt =>
        {
            opt.AdditionalTags["environment"] = "production";
            opt.AdditionalTags["region"] = "us-east-1";
            opt.AdditionalTags["tier"] = "premium";
        });

        // Add staging cache with staging tags
        builder.Services.AddNamedMeteredMemoryCache("staging-cache", meterName: meterName, configureOptions: opt =>
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
        var meterName = SharedUtilities.GetUniqueMeterName("test.eviction.caches");

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(meterName)
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
        builder.Services.TryAddSingleton<Meter>(sp => new Meter(meterName));

        return builder.Build();
    }

    private static IHost CreateHostWithConcurrencyCaches(List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();
        var meterName = SharedUtilities.GetUniqueMeterName("test.concurrency.caches");

        // Add OpenTelemetry with InMemory exporter
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(meterName)
                .AddInMemoryExporter(exportedItems));

        // Add two caches for concurrency testing
        builder.Services.AddNamedMeteredMemoryCache("concurrent-cache-1", meterName: meterName, configureOptions: opt =>
        {
            opt.AdditionalTags["test-type"] = "concurrency";
            opt.AdditionalTags["cache-id"] = "1";
        });

        builder.Services.AddNamedMeteredMemoryCache("concurrent-cache-2", meterName: meterName, configureOptions: opt =>
        {
            opt.AdditionalTags["test-type"] = "concurrency";
            opt.AdditionalTags["cache-id"] = "2";
        });

        return builder.Build();
    }

    private static IHost CreateHostWithSingleMeter(string meterName, List<Metric> exportedItems)
    {
        var builder = new HostApplicationBuilder();

        // Add OpenTelemetry with single meter for complete isolation
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(meterName)
                .AddInMemoryExporter(exportedItems));

        // Add cache with the specified meter
        var cacheName = meterName == "MainMeter" ? "main-cache" : "secondary-cache";
        builder.Services.AddNamedMeteredMemoryCache(cacheName, meterName: meterName);

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
        await Task.Yield();
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

    private static void AssertMetricValueForCache(Metric metric, string cacheName, long expectedValue)
    {
        var metricPoints = new List<MetricPoint>();
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            metricPoints.Add(mp);
        }

        var filteredValue = 0L;
        foreach (var mp in metricPoints)
        {
            var hasCacheTag = false;
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == "cache.name" && tag.Value?.ToString() == cacheName)
                {
                    hasCacheTag = true;
                    break;
                }
            }
            if (hasCacheTag)
            {
                filteredValue += mp.GetSumLong();
            }
        }

        Assert.Equal(expectedValue, filteredValue);
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
}
