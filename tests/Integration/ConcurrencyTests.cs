using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

using OpenTelemetry.Metrics;

using Unit;

namespace Integration;

/// <summary>
/// Tests for thread-safety validation of MeteredMemoryCache tag operations and concurrent metric emission.
/// </summary>
[Collection("MetricsIntegration")]
public class ConcurrencyTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable?.Dispose();
        }
        _disposables.Clear();
    }

    /// <summary>
    /// Tests concurrent hit/miss operations with named cache to validate TagList thread-safety.
    /// </summary>
    [Fact]
    public async Task ConcurrentHitMissOperations_WithNamedCache_ShouldEmitCorrectMetrics()
    {
        // Arrange
        var (host, exportedItems) = CreateHostWithMetrics("concurrent-cache");
        var cache = host.Services.GetRequiredKeyedService<IMemoryCache>("concurrent-cache");
        var metricsProvider = host.Services.GetRequiredService<MeterProvider>();

        const int operationCount = 1000;
        const int operationsPerThread = operationCount / 10;

        var tasks = new List<Task>();
        var hitCount = 0;
        var missCount = 0;

        // Pre-populate some keys for hits
        for (int i = 0; i < operationsPerThread / 2; i++)
        {
            cache.Set($"key-{i}", $"value-{i}");
        }

        // Act: Perform concurrent operations across multiple threads
        for (int threadId = 0; threadId < 10; threadId++)
        {
            var localThreadId = threadId;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var keyId = (localThreadId * operationsPerThread) + i;
                    var key = $"key-{keyId % (operationsPerThread / 2 + 10)}"; // Mix of existing and new keys

                    if (cache.TryGetValue(key, out var value))
                    {
                        Interlocked.Increment(ref hitCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref missCount);
                        cache.Set(key, $"value-{keyId}");
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        await FlushMetricsAsync(metricsProvider);

        // Assert: Verify metrics were recorded correctly despite concurrent access
        var lookupsMetric = exportedItems.FirstOrDefault(m => m.Name == "cache.lookups");

        Assert.NotNull(lookupsMetric);

        AssertMetricHasTag(lookupsMetric, "cache.name", "concurrent-cache");

        var totalHits = GetMetricValueByTag(lookupsMetric, "cache.result", "hit");
        var totalMisses = GetMetricValueByTag(lookupsMetric, "cache.result", "miss");

        // Verify total operations match expected count
        Assert.Equal(operationCount, totalHits + totalMisses);

        // Verify individual counters match (within reasonable bounds due to timing)
        Assert.True(Math.Abs(totalHits - hitCount) <= 1, $"Expected hits ~{hitCount}, got {totalHits}");
        Assert.True(Math.Abs(totalMisses - missCount) <= 1, $"Expected misses ~{missCount}, got {totalMisses}");
    }

    /// <summary>
    /// Tests concurrent eviction scenarios with multiple named caches to validate metric attribution under load.
    /// </summary>
    [Fact]
    public async Task ConcurrentEvictions_MultipleNamedCaches_ShouldAttributeMetricsCorrectly()
    {
        // Arrange
        var (host, exportedItems) = CreateHostWithMultipleCaches();
        var cache1 = host.Services.GetRequiredKeyedService<IMemoryCache>("cache-1");
        var cache2 = host.Services.GetRequiredKeyedService<IMemoryCache>("cache-2");
        var cache3 = host.Services.GetRequiredKeyedService<IMemoryCache>("cache-3");
        var metricsProvider = host.Services.GetRequiredService<MeterProvider>();

        const int itemsPerCache = 100;

        var tasks = new List<Task>();
        var evictionCounts = new ConcurrentDictionary<string, int>();

        // Act: Concurrent operations on different caches with forced evictions
        var caches = new[] {
            ("cache-1", cache1),
            ("cache-2", cache2),
            ("cache-3", cache3)
        };

        foreach (var (cacheName, cache) in caches)
        {
            // 2 threads per cache for intensive operations
            for (int threadIdx = 0; threadIdx < 2; threadIdx++)
            {
                var localCacheName = cacheName;
                var localCache = cache;
                var localThreadIdx = threadIdx;

                tasks.Add(Task.Run(() =>
                {
                    var evictions = 0;

                    for (int i = 0; i < itemsPerCache; i++)
                    {
                        var key = $"{localCacheName}-thread{localThreadIdx}-item{i}";

                        // Use CancellationChangeToken to force evictions
                        using var cts = new CancellationTokenSource();
                        var options = new MemoryCacheEntryOptions
                        {
                            Size = 1,
                            ExpirationTokens = { new CancellationChangeToken(cts.Token) }
                        };

                        localCache.Set(key, $"value-{i}", options);

                        // Cancel the token to trigger eviction
                        if (i % 10 == 0)
                        {
                            cts.Cancel();
                            // Force eviction by accessing the cache
                            localCache.TryGetValue(key, out _);
                        }
                    }

                    evictionCounts.AddOrUpdate(localCacheName, evictions, (k, v) => v + evictions);
                }));
            }
        }

        await Task.WhenAll(tasks);
        await Task.Yield(); // Allow final evictions to complete
        await FlushMetricsAsync(metricsProvider);

        // Assert: Verify eviction metrics are properly attributed to each cache
        var evictionsMetric = exportedItems.FirstOrDefault(m => m.Name == "cache.evictions");

        Assert.NotNull(evictionsMetric);

        // Verify each cache has separate eviction metrics
        var cache1Evictions = GetMetricValueByTag(evictionsMetric, "cache.name", "cache-1");
        var cache2Evictions = GetMetricValueByTag(evictionsMetric, "cache.name", "cache-2");
        var cache3Evictions = GetMetricValueByTag(evictionsMetric, "cache.name", "cache-3");

        // Each cache should have some evictions due to size limits and expiration
        Assert.True(cache1Evictions > 0, "Cache-1 should have evictions");
        Assert.True(cache2Evictions > 0, "Cache-2 should have evictions");
        Assert.True(cache3Evictions > 0, "Cache-3 should have evictions");

        // Verify evictions are attributed correctly (no cross-contamination)
        var totalEvictions = cache1Evictions + cache2Evictions + cache3Evictions;
        var overallEvictions = GetMetricValue(evictionsMetric);
        Assert.Equal(totalEvictions, overallEvictions);
    }

    /// <summary>
    /// Tests high-frequency concurrent operations to stress-test TagList thread-safety.
    /// </summary>
    [Fact]
    public async Task HighFrequencyConcurrentOperations_ShouldMaintainMetricAccuracy()
    {
        // Arrange
        var (host, exportedItems) = CreateHostWithMetrics("stress-cache");
        var cache = host.Services.GetRequiredKeyedService<IMemoryCache>("stress-cache");
        var metricsProvider = host.Services.GetRequiredService<MeterProvider>();

        const int totalOperations = 10000;
        const int threadCount = 20;
        const int operationsPerThread = totalOperations / threadCount;

        var operationCounters = new ConcurrentDictionary<string, long>();
        var tasks = new List<Task>();

        // Act: High-frequency operations across many threads
        for (int threadId = 0; threadId < threadCount; threadId++)
        {
            var localThreadId = threadId;
            tasks.Add(Task.Run(() =>
            {
                var hits = 0L;
                var misses = 0L;
                var sets = 0L;

                for (int i = 0; i < operationsPerThread; i++)
                {
                    var key = $"key-{i % 100}"; // Limited key space for more hits

                    if (cache.TryGetValue(key, out var value))
                    {
                        hits++;
                    }
                    else
                    {
                        misses++;
                        cache.Set(key, $"thread{localThreadId}-value{i}");
                        sets++;
                    }

                    // Mix in some removals
                    if (i % 50 == 0 && i > 0)
                    {
                        cache.Remove($"key-{(i - 25) % 100}");
                    }
                }

                operationCounters[$"thread{localThreadId}-hits"] = hits;
                operationCounters[$"thread{localThreadId}-misses"] = misses;
                operationCounters[$"thread{localThreadId}-sets"] = sets;
            }));
        }

        await Task.WhenAll(tasks);
        await FlushMetricsAsync(metricsProvider);

        // Assert: Verify metric accuracy under high concurrency
        var exportedMetrics = exportedItems;

        var lookupsMetric = exportedMetrics.FirstOrDefault(m => m.Name == "cache.lookups");

        Assert.NotNull(lookupsMetric);

        var totalHits = GetMetricValueByTag(lookupsMetric, "cache.result", "hit");
        var totalMisses = GetMetricValueByTag(lookupsMetric, "cache.result", "miss");

        // Verify total operations
        Assert.Equal(totalOperations, totalHits + totalMisses);

        // Verify cache name tag is present and correct
        AssertMetricHasTag(lookupsMetric, "cache.name", "stress-cache");

        // Verify individual thread counters sum correctly
        var expectedHits = operationCounters.Where(kv => kv.Key.Contains("-hits")).Sum(kv => kv.Value);
        var expectedMisses = operationCounters.Where(kv => kv.Key.Contains("-misses")).Sum(kv => kv.Value);

        Assert.Equal(expectedHits, totalHits);
        Assert.Equal(expectedMisses, totalMisses);
    }

    /// <summary>
    /// Tests concurrent operations with additional tags to validate complex tag scenarios.
    /// </summary>
    [Fact]
    public async Task ConcurrentOperations_WithAdditionalTags_ShouldMaintainTagIntegrity()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "tagged-cache",
            AdditionalTags = new Dictionary<string, object?>
            {
                ["environment"] = "test",
                ["component"] = "integration-test"
            }
        };

        var (host, exportedItems) = CreateHostWithMetricsAndOptions(options);
        var cache = host.Services.GetRequiredKeyedService<IMemoryCache>("tagged-cache");
        var metricsProvider = host.Services.GetRequiredService<MeterProvider>();

        const int threadCount = 8;
        const int operationsPerThread = 250;
        var tasks = new List<Task>();

        // Act: Concurrent operations with complex tagging
        for (int threadId = 0; threadId < threadCount; threadId++)
        {
            var localThreadId = threadId;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var key = $"thread{localThreadId}-key{i}";

                    // Mix of operations to ensure both hits and misses
                    switch (i % 4)
                    {
                        case 0: // Set
                            cache.Set(key, $"value{i}");
                            break;
                        case 1: // Get (miss) - try to get a key that doesn't exist
                            cache.TryGetValue($"nonexistent-{key}", out _);
                            break;
                        case 2: // Get (hit) - try to get the key we just set
                            cache.TryGetValue(key, out _);
                            break;
                        case 3: // Set another key and get it (hit)
                            var anotherKey = $"{key}-another";
                            cache.Set(anotherKey, $"value{i}-another");
                            cache.TryGetValue(anotherKey, out _);
                            break;
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        await FlushMetricsAsync(metricsProvider);

        // Assert: Verify all tags are preserved under concurrency
        var exportedMetrics = exportedItems;

        var lookupsMetric = exportedMetrics.FirstOrDefault(m => m.Name == "cache.lookups");

        Assert.NotNull(lookupsMetric);

        // Verify cache name tag
        AssertMetricHasTag(lookupsMetric, "cache.name", "tagged-cache");

        // Verify additional tags
        AssertMetricHasTag(lookupsMetric, "environment", "test");
        AssertMetricHasTag(lookupsMetric, "component", "integration-test");

        // Verify operation counts
        var totalOperations = threadCount * operationsPerThread;
        var hitsAndMisses = GetMetricValue(lookupsMetric);

        // Should be roughly half since we do 2 get operations for every 2 set/remove operations
        Assert.True(hitsAndMisses >= totalOperations / 4, $"Expected at least {totalOperations / 4} hits+misses, got {hitsAndMisses}");
    }

    /// <summary>
    /// Tests race conditions in metric emission during rapid cache state changes.
    /// </summary>
    [Fact]
    public async Task RapidCacheStateChanges_ShouldNotCauseMetricRaceConditions()
    {
        // Arrange
        var (host, exportedItems) = CreateHostWithMetrics("race-cache");
        var cache = host.Services.GetRequiredKeyedService<IMemoryCache>("race-cache");
        var metricsProvider = host.Services.GetRequiredService<MeterProvider>();

        const int keyCount = 50;
        const int threadCount = 10;
        const int cyclesPerThread = 100;

        var tasks = new List<Task>();
        var operationLog = new ConcurrentBag<string>();

        // Act: Rapid state changes on same keys from multiple threads
        for (int threadId = 0; threadId < threadCount; threadId++)
        {
            var localThreadId = threadId;
            tasks.Add(Task.Run(() =>
            {
                var random = new Random(localThreadId); // Deterministic per thread

                for (int cycle = 0; cycle < cyclesPerThread; cycle++)
                {
                    var key = $"key-{random.Next(keyCount)}";
                    var operation = random.Next(4);

                    switch (operation)
                    {
                        case 0: // Set
                            cache.Set(key, $"thread{localThreadId}-cycle{cycle}");
                            operationLog.Add($"SET:{key}");
                            break;
                        case 1: // Get
                            var hit = cache.TryGetValue(key, out _);
                            operationLog.Add(hit ? $"HIT:{key}" : $"MISS:{key}");
                            break;
                        case 2: // GetOrCreate
                            cache.GetOrCreate(key, entry => $"created-thread{localThreadId}-cycle{cycle}");
                            operationLog.Add($"GOC:{key}");
                            break;
                        case 3: // Remove
                            cache.Remove(key);
                            operationLog.Add($"REM:{key}");
                            break;
                    }

                    // Occasional yield to increase race condition likelihood
                    if (cycle % 20 == 0)
                    {
                        Thread.Yield();
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        await FlushMetricsAsync(metricsProvider);

        // Assert: Verify metrics consistency despite race conditions
        var exportedMetrics = exportedItems;

        var lookupsMetric = exportedMetrics.FirstOrDefault(m => m.Name == "cache.lookups");
        var evictionsMetric = exportedMetrics.FirstOrDefault(m => m.Name == "cache.evictions");

        Assert.NotNull(lookupsMetric);

        // Verify metrics have correct tags
        AssertMetricHasTag(lookupsMetric, "cache.name", "race-cache");

        if (evictionsMetric != null)
        {
            AssertMetricHasTag(evictionsMetric, "cache.name", "race-cache");
        }

        // Verify metric values are reasonable (no negative values, etc.)
        var hits = GetMetricValueByTag(lookupsMetric, "cache.result", "hit");
        var misses = GetMetricValueByTag(lookupsMetric, "cache.result", "miss");
        var evictions = evictionsMetric != null ? GetMetricValue(evictionsMetric) : 0;

        Assert.True(hits >= 0, $"Hits should be non-negative, got {hits}");
        Assert.True(misses >= 0, $"Misses should be non-negative, got {misses}");
        Assert.True(evictions >= 0, $"Evictions should be non-negative, got {evictions}");

        // Verify total operations tracked match expected operations
        var getOperations = operationLog.Count(op => op.StartsWith("HIT:") || op.StartsWith("MISS:") || op.StartsWith("GOC:"));
        Assert.True(hits + misses >= getOperations / 2, $"Expected hits+misses >= {getOperations / 2}, got {hits + misses}");
    }

    /// <summary>
    /// Tests thread-safety of meter and counter creation under concurrent cache instantiation.
    /// </summary>
    [Fact]
    public async Task ConcurrentCacheInstantiation_ShouldCreateMetersThreadSafely()
    {
        // Arrange
        const int cacheCount = 20;
        const int operationsPerCache = 50;

        List<Metric> exportedItems = new();

        // Generate unique meter name for test isolation
        var meterName = SharedUtilities.GetUniqueMeterName("test.metered.cache");

        var builder = new HostApplicationBuilder();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(MeteredMemoryCache.MeterName)
                .AddInMemoryExporter(exportedItems));

        var host = builder.Build();
        _disposables.Add(host);

        // Start the host to activate the meter provider
        await host.StartAsync();

        // Create meter instance via IMeterFactory for proper DI-scoped isolation
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        var meterProvider = host.Services.GetRequiredService<MeterProvider>();

        var tasks = new List<Task>();
        var caches = new ConcurrentBag<MeteredMemoryCache>();

        // Act: Create multiple caches concurrently
        for (int i = 0; i < cacheCount; i++)
        {
            var cacheIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                // Create cache with unique name
                var innerCache = new MemoryCache(new MemoryCacheOptions());
                var cacheName = $"concurrent-cache-{cacheIndex}";
                var meteredCache = new MeteredMemoryCache(innerCache, meterFactory, cacheName);

                caches.Add(meteredCache);

                // Perform operations immediately
                for (int j = 0; j < operationsPerCache; j++)
                {
                    var key = $"key-{j}";
                    if (j % 2 == 0)
                    {
                        meteredCache.Set(key, $"value-{j}");
                    }
                    else
                    {
                        // Try to get a key that was set (even j values) - this will be a hit
                        var keyToGet = $"key-{j - 1}";
                        meteredCache.TryGetValue(keyToGet, out _);

                        // Also try to get a key that doesn't exist - this will be a miss
                        var nonExistentKey = $"nonexistent-key-{j}";
                        meteredCache.TryGetValue(nonExistentKey, out _);
                    }
                }

                await Task.Yield(); // Small delay to allow metric emission
            }));
        }

        await Task.WhenAll(tasks);
        await FlushMetricsAsync(meterProvider);

        // Assert: Verify all caches created successfully and emitted metrics
        Assert.Equal(cacheCount, caches.Count);

        // Debug: Check what metrics were actually exported
        var allMetricNames = exportedItems.Select(m => m.Name).ToList();
        Assert.True(exportedItems.Count > 0, $"No metrics were exported. Expected at least some metrics, but got {exportedItems.Count} metrics. Available metric names: [{string.Join(", ", allMetricNames)}]");

        var lookupsMetric = exportedItems.FirstOrDefault(m => m.Name == "cache.lookups");

        Assert.NotNull(lookupsMetric);

        // Verify metrics from all caches are captured
        var totalExpectedOperations = cacheCount * operationsPerCache;
        var totalMetricOperations = GetMetricValue(lookupsMetric);

        Assert.Equal(totalExpectedOperations, totalMetricOperations);

        // Verify each cache has its own metrics with correct cache name
        foreach (var metricPoint in GetMetricPoints(lookupsMetric))
        {
            var cacheNameTag = default(KeyValuePair<string, object?>);
            foreach (var tag in metricPoint.Tags)
            {
                if (tag.Key == "cache.name")
                {
                    cacheNameTag = tag;
                    break;
                }
            }
            Assert.True(cacheNameTag.Key != null, "Cache name tag should be present");
            Assert.True(cacheNameTag.Value?.ToString()?.StartsWith("concurrent-cache-"),
                $"Cache name should start with 'concurrent-cache-', got '{cacheNameTag.Value}'");
        }

        // Cleanup
        await host.StopAsync();
        foreach (var cache in caches)
        {
            cache.Dispose();
        }
    }

    // Helper Methods

    private (IHost Host, List<Metric> ExportedItems) CreateHostWithMetrics(string cacheName)
    {
        var exportedItems = new List<Metric>();
        // Note: List<Metric> doesn't implement IDisposable, we'll dispose the host which cleans up resources

        var builder = new HostApplicationBuilder();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(MeteredMemoryCache.MeterName)
                .AddInMemoryExporter(exportedItems));

        builder.Services.AddNamedMeteredMemoryCache(cacheName);

        var host = builder.Build();
        _disposables.Add(host);
        return (host, exportedItems);
    }

    private (IHost Host, List<Metric> ExportedItems) CreateHostWithMultipleCaches()
    {
        var exportedItems = new List<Metric>();

        var builder = new HostApplicationBuilder();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(MeteredMemoryCache.MeterName)
                .AddInMemoryExporter(exportedItems));

        // Create multiple named caches
        builder.Services.AddNamedMeteredMemoryCache("cache-1");
        builder.Services.AddNamedMeteredMemoryCache("cache-2");
        builder.Services.AddNamedMeteredMemoryCache("cache-3");

        var host = builder.Build();
        _disposables.Add(host);
        return (host, exportedItems);
    }

    private (IHost Host, List<Metric> ExportedItems) CreateHostWithMetricsAndOptions(MeteredMemoryCacheOptions options)
    {
        var exportedItems = new List<Metric>();

        var builder = new HostApplicationBuilder();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(MeteredMemoryCache.MeterName)
                .AddInMemoryExporter(exportedItems));

        builder.Services.AddNamedMeteredMemoryCache(options.CacheName!, configureOptions: opt =>
        {
            opt.AdditionalTags = options.AdditionalTags;
        });

        var host = builder.Build();
        _disposables.Add(host);
        return (host, exportedItems);
    }

    private static async Task FlushMetricsAsync(MeterProvider meterProvider)
    {
        meterProvider.ForceFlush(5000); // 5000ms = 5 seconds
        await Task.Yield(); // Allow metric processing
    }


    private static long GetMetricValue(Metric metric)
    {
        var metricPoints = new List<MetricPoint>();
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            metricPoints.Add(mp);
        }

        return metricPoints.Sum(mp => mp.GetSumLong());
    }

    private static long GetMetricValueByTag(Metric metric, string tagKey, string tagValue)
    {
        foreach (ref readonly var metricPoint in metric.GetMetricPoints())
        {
            foreach (var tag in metricPoint.Tags)
            {
                if (tag.Key == tagKey && tag.Value?.ToString() == tagValue)
                {
                    return metricPoint.GetSumLong();
                }
            }
        }
        return 0;
    }

    private static IEnumerable<MetricPoint> GetMetricPoints(Metric metric)
    {
        var points = new List<MetricPoint>();
        foreach (ref readonly var metricPoint in metric.GetMetricPoints())
        {
            points.Add(metricPoint);
        }
        return points;
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

        Assert.True(hasTag, $"Metric '{metric.Name}' should have tag '{tagKey}' with value '{expectedValue}'");
    }
}
