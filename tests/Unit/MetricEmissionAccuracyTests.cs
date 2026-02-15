using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

using Unit.Shared;

namespace Unit;

/// <summary>
/// Comprehensive validation of metric emission accuracy for <see cref="MeteredMemoryCache"/>.
/// Tests exact metric counts, tag accuracy, and eviction tracking with deterministic scenarios.
/// </summary>
public class MetricEmissionAccuracyTests
{

    [Fact]
    public void HitMissRatio_ExactCounting_ValidatesAccuracy()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.1"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "accuracy-test");

        // Execute precise sequence: 3 misses, 5 hits
        cache.TryGetValue("key1", out _); // miss 1
        cache.TryGetValue("key2", out _); // miss 2  
        cache.TryGetValue("key3", out _); // miss 3

        cache.Set("key1", "value1");
        cache.Set("key2", "value2");
        cache.Set("key3", "value3");

        cache.TryGetValue("key1", out _); // hit 1
        cache.TryGetValue("key2", out _); // hit 2
        cache.TryGetValue("key1", out _); // hit 3
        cache.TryGetValue("key3", out _); // hit 4
        cache.TryGetValue("key2", out _); // hit 5

        // Validate exact counts
        Assert.Equal(5, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));
        Assert.Equal(3, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));

        // Validate all measurements have correct cache name tag
        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("cache.name", "accuracy-test"));
    }

    [Fact]
    public async Task EvictionMetrics_DeterministicScenario_ValidatesAccuracyAndTags()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.2"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.evictions");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "eviction-test");

        // Create entries with different eviction scenarios
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        // Scenario 1: TokenExpired eviction
        var options1 = new MemoryCacheEntryOptions();
        options1.AddExpirationToken(new CancellationChangeToken(cts1.Token));
        cache.Set("expiring-key1", "value1", options1);

        // Scenario 2: Another TokenExpired eviction
        var options2 = new MemoryCacheEntryOptions();
        options2.AddExpirationToken(new CancellationChangeToken(cts2.Token));
        cache.Set("expiring-key2", "value2", options2);

        // Scenario 3: Manual removal - excluded from eviction metrics per dotnet/runtime#124140
        // (Evictions only count automatic removals: expiration, memory pressure, size limits)
        cache.Set("manual-remove-key", "value3");

        // Trigger evictions
        cts1.Cancel(); // Expires expiring-key1
        cts2.Cancel(); // Expires expiring-key2

        // Force eviction processing
        cache.TryGetValue("expiring-key1", out _); // Should not be found, triggers cleanup
        cache.TryGetValue("expiring-key2", out _); // Should not be found, triggers cleanup
        inner.Compact(0.0); // Force compact to process expired entries

        cache.Remove("manual-remove-key"); // Does NOT trigger eviction metric (explicit removal excluded)

        // Use deterministic wait for eviction counter to reach expected value
        var evictionWaitSucceeded = await harness.WaitForMetricAsync("cache.evictions", 2, TimeSpan.FromSeconds(5));
        Assert.True(evictionWaitSucceeded, "Expected eviction count >= 2 within timeout");

        // With Observable instruments, eviction count is reported as a single aggregate
        harness.AssertAggregatedCount("cache.evictions", 2);

        // Validate eviction measurements have cache.name tag
        var evictionMeasurements = harness.GetMeasurements("cache.evictions");
        Assert.All(evictionMeasurements, measurement =>
        {
            Assert.True(measurement.Tags.ContainsKey("cache.name"), "Eviction measurement missing 'cache.name' tag");
            Assert.Equal("eviction-test", measurement.Tags["cache.name"]);
        });
    }

    [Fact]
    public void MultipleNamedCaches_IsolatedMetrics_ValidatesTagSegregation()
    {
        using var inner1 = new MemoryCache(new MemoryCacheOptions());
        using var inner2 = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.3"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");

        using var cache1 = new MeteredMemoryCache(inner1, meter, cacheName: "cache-alpha");
        using var cache2 = new MeteredMemoryCache(inner2, meter, cacheName: "cache-beta");

        // Generate distinct patterns for each cache
        // Cache1: 2 misses, 3 hits
        cache1.TryGetValue("k1", out _); // miss
        cache1.TryGetValue("k2", out _); // miss
        cache1.Set("k1", "v1");
        cache1.Set("k2", "v2");
        cache1.TryGetValue("k1", out _); // hit
        cache1.TryGetValue("k2", out _); // hit  
        cache1.TryGetValue("k1", out _); // hit

        // Cache2: 4 misses, 1 hit
        cache2.TryGetValue("x1", out _); // miss
        cache2.TryGetValue("x2", out _); // miss
        cache2.TryGetValue("x3", out _); // miss
        cache2.TryGetValue("x4", out _); // miss
        cache2.Set("x1", "y1");
        cache2.TryGetValue("x1", out _); // hit

        // Validate cache1 metrics isolation
        var cache1Hits = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "cache-alpha"));
        var cache1Misses = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"),
            new KeyValuePair<string, object?>("cache.name", "cache-alpha"));

        Assert.Equal(3, cache1Hits);
        Assert.Equal(2, cache1Misses);

        // Validate cache2 metrics isolation  
        var cache2Hits = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "cache-beta"));
        var cache2Misses = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"),
            new KeyValuePair<string, object?>("cache.name", "cache-beta"));

        Assert.Equal(1, cache2Hits);
        Assert.Equal(4, cache2Misses);

        // Validate total aggregation - expected: cache1(3 hits) + cache2(1 hit) = 4 total hits
        // expected: cache1(2 misses) + cache2(4 misses) = 6 total misses  
        Assert.Equal(4, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"))); // 3 + 1
        Assert.Equal(6, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"))); // 2 + 4

        // Validate no cross-contamination of cache names
        var allHitMeasurements = harness.GetMeasurementsWithTags("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"));
        var allMissMeasurements = harness.GetMeasurementsWithTags("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"));

        Assert.All(allHitMeasurements.Concat(allMissMeasurements), measurement =>
        {
            var cacheName = measurement.Tags["cache.name"]?.ToString();
            Assert.True(cacheName == "cache-alpha" || cacheName == "cache-beta",
                $"Unexpected cache name: {cacheName}");
        });
    }

    [Fact]
    public void AdditionalTags_AccurateEmission_ValidatesCustomTagPropagation()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.4"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "tagged-cache",
            AdditionalTags =
            {
                ["environment"] = "test",
                ["region"] = "us-west-2",
                ["version"] = "1.0.0"
            }
        };

        using var cache = new MeteredMemoryCache(inner, meter, options);

        // Generate metrics
        cache.TryGetValue("key", out _); // miss
        cache.Set("key", "value");
        cache.TryGetValue("key", out _); // hit

        // Validate all expected tags are present on every measurement
        var allMeasurements = harness.AllMeasurements;
        Assert.All(allMeasurements, measurement =>
        {
            // Validate core tags
            Assert.Equal("tagged-cache", measurement.Tags["cache.name"]);
            Assert.Equal("test", measurement.Tags["environment"]);
            Assert.Equal("us-west-2", measurement.Tags["region"]);
            Assert.Equal("1.0.0", measurement.Tags["version"]);
        });

        // Validate minimum expected tags are present (flexible for future tag additions)
        Assert.All(allMeasurements, measurement =>
        {
            Assert.InRange(measurement.Tags.Count, 4, int.MaxValue); // At least cache.name + 3 additional tags
        });

        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));
    }

    [Fact]
    public void GetOrCreateMethod_AccurateMetrics_ValidatesFactoryScenarios()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.5"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "getorcreate-test");

        var factoryCallCount = 0;
        string TestFactory(ICacheEntry entry)
        {
            factoryCallCount++;
            return $"created-value-{factoryCallCount}";
        }

        // First call: should be miss + factory execution
        var result1 = cache.GetOrCreate("key1", TestFactory);
        Assert.Equal("created-value-1", result1);
        Assert.Equal(1, factoryCallCount);

        // Second call: should be hit, no factory execution
        var result2 = cache.GetOrCreate("key1", TestFactory);
        Assert.Equal("created-value-1", result2); // Same value
        Assert.Equal(1, factoryCallCount); // Factory not called again

        // Third call with different key: should be miss + factory execution
        var result3 = cache.GetOrCreate("key2", TestFactory);
        Assert.Equal("created-value-2", result3);
        Assert.Equal(2, factoryCallCount);

        // Validate exact metric counts
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));
        Assert.Equal(2, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));

        // Validate cache name tags
        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("cache.name", "getorcreate-test"));
    }

    [Fact]
    public void TryGetStronglyTyped_AccurateMetrics_ValidatesTypeConversion()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.tryget.typed.validation"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "tryget-typed-test");

        // Store different types
        cache.Set("string-key", "test-string");
        cache.Set("int-key", 42);
        cache.Set("object-key", new { Name = "test" });

        // Test successful type conversions (hits)
        var stringSuccess = cache.TryGetValue<string>("string-key", out var stringValue);
        Assert.True(stringSuccess);
        Assert.Equal("test-string", stringValue);

        var intSuccess = cache.TryGetValue<int>("int-key", out var intValue);
        Assert.True(intSuccess);
        Assert.Equal(42, intValue);

        // Test type mismatch - the underlying IMemoryCache.TryGetValue() returns true (key exists),
        // triggering a "hit" metric, even though TryGetValue<T> returns false due to type mismatch.
        var typeMismatch = cache.TryGetValue<int>("string-key", out var mismatchValue);
        Assert.False(typeMismatch);
        Assert.Equal(0, mismatchValue); // default int

        // Test missing key (miss)
        var missing = cache.TryGetValue<string>("nonexistent-key", out var missingValue);
        Assert.False(missing);
        Assert.Null(missingValue);

        // Validate metrics: 3 hits (including type mismatch since key exists), 1 miss.
        // TryGetValue<T> extension calls IMemoryCache.TryGetValue first (triggering hit/miss),
        // then performs the type check - so a type mismatch still counts as a hit.
        Assert.Equal(3, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));

        // Validate cache name tags
        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("cache.name", "tryget-typed-test"));
    }

    [Fact]
    public async Task CreateEntryMethod_AccurateEvictionRegistration_ValidatesCallbackSetup()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.7"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.evictions");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "createentry-test");

        // Create entry manually and dispose to trigger eviction
        using (var entry = cache.CreateEntry("manual-entry"))
        {
            entry.Value = "test-value";
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1);
        }

        // Try multiple access attempts to trigger cleanup with deterministic waiting
        for (int i = 0; i < 5; i++)
        {
            cache.TryGetValue("manual-entry", out _);
            cache.TryGetValue($"trigger-cleanup-{i}", out _); // Additional access to trigger internal cleanup

            // Check if eviction has been recorded yet
            if (harness.GetMeasurements("cache.evictions").Any())
            {
                break; // Early exit if eviction detected
            }
            await Task.Yield(); // Yield between attempts
        }

        // Force compact multiple times to ensure eviction processing
        inner.Compact(0.0);
        inner.Compact(0.5);

        // Use deterministic wait for eviction callback processing
        var evictionDetected = await harness.WaitForMetricAsync("cache.evictions", 1, TimeSpan.FromSeconds(3));

        // Additional trigger attempts if not detected yet
        if (!evictionDetected)
        {
            cache.TryGetValue("manual-entry", out _);
            await harness.WaitForMetricAsync("cache.evictions", 1, TimeSpan.FromSeconds(2));
        }

        // Validate eviction was recorded
        var evictions = harness.GetMeasurements("cache.evictions");
        Assert.True(evictions.Count >= 1, $"Expected at least 1 eviction measurement, got {evictions.Count}");

        // Validate eviction tags (Observable instruments use pre-allocated base tags, no per-eviction reason)
        Assert.All(evictions, eviction =>
        {
            Assert.True(eviction.Tags.ContainsKey("cache.name"));
            Assert.Equal("createentry-test", eviction.Tags["cache.name"]);
        });
    }

    [Fact]
    public void ZeroMeasurements_EdgeCase_ValidatesNoFalsePositives()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.8"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests", "cache.evictions");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "zero-test");

        // Don't perform any cache operations

        // Validate all counters are zero
        Assert.Equal(0, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));
        Assert.Equal(0, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));
        harness.AssertAggregatedCount("cache.evictions", 0);
    }

    [Fact]
    public async Task ComprehensiveMultiCacheScenario_CompleteIsolationValidation_ValidatesAllOperations()
    {
        using var inner1 = new MemoryCache(new MemoryCacheOptions());
        using var inner2 = new MemoryCache(new MemoryCacheOptions());
        using var inner3 = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.multicache"));
        using var harness = new MetricCollectionHarness(meter.Name,
            "cache.requests", "cache.evictions");

        // Create 3 caches with different configurations
        var options1 = new MeteredMemoryCacheOptions
        {
            CacheName = "service-cache",
            AdditionalTags = { ["service"] = "user-service", ["environment"] = "test" }
        };

        var options2 = new MeteredMemoryCacheOptions
        {
            CacheName = "data-cache",
            AdditionalTags = { ["service"] = "data-service", ["tier"] = "backend" }
        };

        using var cache1 = new MeteredMemoryCache(inner1, meter, options1);
        using var cache2 = new MeteredMemoryCache(inner2, meter, options2);
        using var cache3 = new MeteredMemoryCache(inner3, meter, cacheName: "simple-cache"); // Basic configuration

        // Scenario 1: Different hit/miss patterns per cache
        // Cache1: 2 hits, 1 miss
        cache1.Set("user:1", "data1");
        cache1.Set("user:2", "data2");
        cache1.TryGetValue("user:1", out _); // hit
        cache1.TryGetValue("user:2", out _); // hit
        cache1.TryGetValue("user:3", out _); // miss

        // Cache2: 1 hit, 3 misses
        cache2.TryGetValue("query:1", out _); // miss
        cache2.TryGetValue("query:2", out _); // miss
        cache2.Set("query:1", "result1");
        cache2.TryGetValue("query:1", out _); // hit
        cache2.TryGetValue("query:3", out _); // miss

        // Cache3: 3 hits, 2 misses
        cache3.Set("key1", "value1");
        cache3.Set("key2", "value2");
        cache3.Set("key3", "value3");
        cache3.TryGetValue("key1", out _); // hit
        cache3.TryGetValue("key2", out _); // hit
        cache3.TryGetValue("key3", out _); // hit
        cache3.TryGetValue("key4", out _); // miss
        cache3.TryGetValue("key5", out _); // miss

        // Scenario 2: Eviction scenarios for each cache
        // Use CancellationChangeToken for immediate expiration
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        using (var entry1 = cache1.CreateEntry("temp:1"))
        {
            entry1.Value = "temp-data";
            entry1.AddExpirationToken(new CancellationChangeToken(cts1.Token));
        }

        using (var entry2 = cache2.CreateEntry("temp:2"))
        {
            entry2.Value = "temp-query";
            entry2.AddExpirationToken(new CancellationChangeToken(cts2.Token));
        }

        using (var entry3 = cache3.CreateEntry("will-expire"))
        {
            entry3.Value = "will-expire-data";
            entry3.AddExpirationToken(new CancellationChangeToken(cts3.Token));
        }

        // Trigger expirations (not manual Remove, which is excluded from eviction metrics per dotnet/runtime#124140)
        cts1.Cancel();
        cts2.Cancel();
        cts3.Cancel();
        inner1.Compact(0.0);
        inner2.Compact(0.0);
        inner3.Compact(0.0);

        // Wait for eviction metrics
        var evictionDetected = await harness.WaitForMetricAsync("cache.evictions", 3, TimeSpan.FromSeconds(5));
        Assert.True(evictionDetected, "Expected eviction metrics from all 3 caches");

        // Validate hit/miss isolation per cache
        var cache1Hits = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "service-cache"));
        var cache1Misses = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"),
            new KeyValuePair<string, object?>("cache.name", "service-cache"));

        var cache2Hits = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "data-cache"));
        var cache2Misses = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"),
            new KeyValuePair<string, object?>("cache.name", "data-cache"));

        var cache3Hits = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "simple-cache"));
        var cache3Misses = harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss"),
            new KeyValuePair<string, object?>("cache.name", "simple-cache"));

        // Assert expected counts
        Assert.Equal(2, cache1Hits);
        Assert.Equal(1, cache1Misses);
        Assert.Equal(1, cache2Hits);
        Assert.Equal(3, cache2Misses);
        Assert.Equal(3, cache3Hits);
        Assert.Equal(2, cache3Misses);

        // Validate eviction isolation
        var cache1Evictions = harness.GetMeasurementsWithTags("cache.evictions",
            new KeyValuePair<string, object?>("cache.name", "service-cache"));
        var cache2Evictions = harness.GetMeasurementsWithTags("cache.evictions",
            new KeyValuePair<string, object?>("cache.name", "data-cache"));
        var cache3Evictions = harness.GetMeasurementsWithTags("cache.evictions",
            new KeyValuePair<string, object?>("cache.name", "simple-cache"));

        Assert.True(cache1Evictions.Count >= 1, "Cache1 should have eviction metrics");
        Assert.True(cache2Evictions.Count >= 1, "Cache2 should have eviction metrics");
        Assert.True(cache3Evictions.Count >= 1, "Cache3 should have eviction metrics");

        // Validate additional tags isolation
        var cache1Measurements = harness.GetMeasurementsWithTags("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "service-cache"));
        var cache2Measurements = harness.GetMeasurementsWithTags("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit"),
            new KeyValuePair<string, object?>("cache.name", "data-cache"));

        // Verify cache1 has service-specific tags
        Assert.All(cache1Measurements, m =>
        {
            Assert.Equal("user-service", m.Tags["service"]);
            Assert.Equal("test", m.Tags["environment"]);
        });

        // Verify cache2 has different service-specific tags
        Assert.All(cache2Measurements, m =>
        {
            Assert.Equal("data-service", m.Tags["service"]);
            Assert.Equal("backend", m.Tags["tier"]);
        });

        // Validate no cross-contamination: cache names should be distinct
        var allMeasurements = harness.AllMeasurements;
        var cacheNames = allMeasurements
            .Where(m => m.Tags.ContainsKey("cache.name"))
            .Select(m => m.Tags["cache.name"]?.ToString())
            .Distinct()
            .ToList();

        Assert.Equal(3, cacheNames.Count);
        Assert.Contains("service-cache", cacheNames);
        Assert.Contains("data-cache", cacheNames);
        Assert.Contains("simple-cache", cacheNames);

        // Validate total aggregation
        var totalHits = cache1Hits + cache2Hits + cache3Hits;
        var totalMisses = cache1Misses + cache2Misses + cache3Misses;

        Assert.Equal(totalHits, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));
        Assert.Equal(totalMisses, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));
    }

    [Fact]
    public void HighVolumeOperations_AccurateAggregation_ValidatesScalability()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.9"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: "volume-test");

        const int operationCount = 1000;

        // Generate high volume of mixed operations
        for (int i = 0; i < operationCount; i++)
        {
            var key = $"key-{i % 100}"; // Reuse keys to generate hits

            if (i < 100)
            {
                // First 100 operations will be misses (new keys)
                cache.TryGetValue(key, out _);
            }
            else
            {
                // Set values for first 100 keys
                if (i < 200)
                {
                    cache.Set(key, $"value-{i}");
                }
                else
                {
                    // Remaining operations will be hits (existing keys)
                    cache.TryGetValue(key, out _);
                }
            }
        }

        // Validate exact counts: 100 misses + 800 hits = 900 total operations
        Assert.Equal(100, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "miss")));
        Assert.Equal(800, harness.GetAggregatedCount("cache.requests", new KeyValuePair<string, object?>("cache.request.type", "hit")));

        // Validate measurements have correct cache name
        var allMeasurements = harness.AllMeasurements;
        Assert.All(allMeasurements.Where(m => m.Value > 0), m =>
            Assert.Equal("volume-test", m.Tags["cache.name"]));
    }
}
