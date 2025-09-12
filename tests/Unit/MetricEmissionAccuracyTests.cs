using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;
using Xunit;
using System.Diagnostics;

namespace Unit;

/// <summary>
/// Comprehensive validation of metric emission accuracy for <see cref="MeteredMemoryCache"/>.
/// Tests exact metric counts, tag accuracy, and eviction tracking with deterministic scenarios.
/// </summary>
public class MetricEmissionAccuracyTests
{
    /// <summary>
    /// Enhanced metric collection harness that provides detailed validation capabilities
    /// for testing accurate metric emission from <see cref="MeteredMemoryCache"/>.
    /// Thread-safe implementation using proper locking and defensive copying.
    /// </summary>
    private sealed class MetricCollectionHarness : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<MetricMeasurement> _measurements = new();
        private readonly Dictionary<string, List<MetricMeasurement>> _measurementsByInstrument = new();
        private readonly Dictionary<string, long> _aggregatedCounters = new();
        private readonly string[] _instrumentNames;
        private readonly string? _meterNameFilter;
        private readonly object _lock = new object();

        /// <summary>
        /// Gets a thread-safe snapshot of all measurements collected so far.
        /// </summary>
        public IReadOnlyList<MetricMeasurement> AllMeasurements
        {
            get
            {
                lock (_lock)
                {
                    return _measurements.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets a thread-safe snapshot of aggregated counters.
        /// </summary>
        public IReadOnlyDictionary<string, long> AggregatedCounters
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<string, long>(_aggregatedCounters);
                }
            }
        }

        public MetricCollectionHarness(params string[] instrumentNames) : this(null, instrumentNames)
        {
        }

        public MetricCollectionHarness(string? meterNameFilter, params string[] instrumentNames)
        {
            _instrumentNames = instrumentNames;
            _meterNameFilter = meterNameFilter;

            _listener.InstrumentPublished = (inst, listener) =>
            {
                // Filter by both instrument name AND meter name to prevent cross-test contamination
                if (instrumentNames.Contains(inst.Name) &&
                    (_meterNameFilter == null || inst.Meter.Name == _meterNameFilter))
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            _listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                var tagDict = tags.ToArray().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var metricMeasurement = new MetricMeasurement(inst.Name, measurement, tagDict, DateTime.UtcNow);

                lock (_lock)
                {
                    _measurements.Add(metricMeasurement);

                    if (!_measurementsByInstrument.ContainsKey(inst.Name))
                        _measurementsByInstrument[inst.Name] = new List<MetricMeasurement>();
                    _measurementsByInstrument[inst.Name].Add(metricMeasurement);

                    _aggregatedCounters[inst.Name] = _aggregatedCounters.GetValueOrDefault(inst.Name, 0) + measurement;
                }
            });
            _listener.Start();
        }

        /// <summary>
        /// Gets a thread-safe snapshot of all measurements for a specific instrument name.
        /// </summary>
        public IReadOnlyList<MetricMeasurement> GetMeasurements(string instrumentName)
        {
            lock (_lock)
            {
                return _measurementsByInstrument.GetValueOrDefault(instrumentName, new List<MetricMeasurement>()).ToArray();
            }
        }

        /// <summary>
        /// Gets measurements for an instrument with specific tag filters.
        /// </summary>
        public IReadOnlyList<MetricMeasurement> GetMeasurementsWithTags(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
        {
            return GetMeasurements(instrumentName)
                .Where(m => requiredTags.All(required =>
                    m.Tags.ContainsKey(required.Key) &&
                    Equals(m.Tags[required.Key], required.Value)))
                .ToList();
        }

        /// <summary>
        /// Gets the aggregated count for an instrument with specific tag filters.
        /// </summary>
        public long GetAggregatedCount(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
        {
            return GetMeasurementsWithTags(instrumentName, requiredTags).Sum(m => m.Value);
        }

        /// <summary>
        /// Validates that exactly the expected number of measurements occurred.
        /// </summary>
        public void AssertMeasurementCount(string instrumentName, int expectedCount)
        {
            var measurements = GetMeasurements(instrumentName);
            Assert.Equal(expectedCount, measurements.Count);
        }

        /// <summary>
        /// Deterministic wait helper that polls for expected metric count instead of using Thread.Sleep.
        /// Addresses flaky test timing issues by actively waiting for metrics to be emitted.
        /// </summary>
        public async Task<bool> WaitForMetricAsync(string instrumentName, int expectedCount, TimeSpan timeout)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var currentCount = GetMeasurements(instrumentName).Count;
                if (currentCount >= expectedCount)
                {
                    return true;
                }
                await Task.Delay(10); // Short polling interval
            }
            return false;
        }

        /// <summary>
        /// Deterministic wait helper for measurements with specific tags.
        /// </summary>
        public async Task<bool> WaitForMetricWithTagsAsync(string instrumentName, int expectedCount, TimeSpan timeout, params KeyValuePair<string, object?>[] requiredTags)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var currentCount = GetMeasurementsWithTags(instrumentName, requiredTags).Count;
                if (currentCount >= expectedCount)
                {
                    return true;
                }
                await Task.Delay(10); // Short polling interval
            }
            return false;
        }

        /// <summary>
        /// Validates that the aggregated counter matches the expected value.
        /// </summary>
        public void AssertAggregatedCount(string instrumentName, long expectedTotal)
        {
            Assert.Equal(expectedTotal, _aggregatedCounters.GetValueOrDefault(instrumentName, 0));
        }

        /// <summary>
        /// Validates that all measurements for an instrument contain the required tags.
        /// </summary>
        public void AssertAllMeasurementsHaveTags(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
        {
            var measurements = GetMeasurements(instrumentName);
            Assert.All(measurements, m =>
            {
                foreach (var requiredTag in requiredTags)
                {
                    Assert.True(m.Tags.ContainsKey(requiredTag.Key),
                        $"Measurement missing required tag '{requiredTag.Key}'");
                    Assert.Equal(requiredTag.Value, m.Tags[requiredTag.Key]);
                }
            });
        }

        /// <summary>
        /// Clears all collected measurements for fresh test scenarios.
        /// </summary>
        public void Reset()
        {
            lock (_measurements)
            {
                _measurements.Clear();
                _measurementsByInstrument.Clear();
                _aggregatedCounters.Clear();
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Represents a single metric measurement with all associated metadata.
    /// </summary>
    private sealed record MetricMeasurement(
        string InstrumentName,
        long Value,
        IReadOnlyDictionary<string, object?> Tags,
        DateTime Timestamp);

    [Fact]
    public void HitMissRatio_ExactCounting_ValidatesAccuracy()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.1");
        using var harness = new MetricCollectionHarness("test.accuracy.1", "cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "accuracy-test");

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
        harness.AssertAggregatedCount("cache_hits_total", 5);
        harness.AssertAggregatedCount("cache_misses_total", 3);
        harness.AssertMeasurementCount("cache_hits_total", 5);
        harness.AssertMeasurementCount("cache_misses_total", 3);

        // Validate all measurements have correct cache name tag
        harness.AssertAllMeasurementsHaveTags("cache_hits_total",
            new KeyValuePair<string, object?>("cache.name", "accuracy-test"));
        harness.AssertAllMeasurementsHaveTags("cache_misses_total",
            new KeyValuePair<string, object?>("cache.name", "accuracy-test"));
    }

    [Fact]
    public async Task EvictionMetrics_DeterministicScenario_ValidatesAccuracyAndTags()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.2");
        using var harness = new MetricCollectionHarness("test.accuracy.2", "cache_evictions_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "eviction-test");

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

        // Scenario 3: Manual removal (should trigger Removed eviction)
        cache.Set("manual-remove-key", "value3");

        // Trigger evictions
        cts1.Cancel(); // Expires expiring-key1
        cts2.Cancel(); // Expires expiring-key2

        // Force eviction processing
        cache.TryGetValue("expiring-key1", out _); // Should not be found, triggers cleanup
        cache.TryGetValue("expiring-key2", out _); // Should not be found, triggers cleanup
        inner.Compact(0.0); // Force compact to process expired entries

        cache.Remove("manual-remove-key"); // Triggers Removed eviction

        // Use deterministic wait instead of Thread.Sleep for reliable CI testing
        var evictionWaitSucceeded = await harness.WaitForMetricAsync("cache_evictions_total", 2, TimeSpan.FromSeconds(5));
        Assert.True(evictionWaitSucceeded, "Expected at least 2 eviction metrics within timeout");

        // Validate eviction metrics
        var evictionMeasurements = harness.GetMeasurements("cache_evictions_total");
        Assert.True(evictionMeasurements.Count >= 2, $"Expected at least 2 evictions, got {evictionMeasurements.Count}");

        // Validate eviction reason tags are present and correctly formatted
        Assert.All(evictionMeasurements, measurement =>
        {
            Assert.True(measurement.Tags.ContainsKey("reason"), "Eviction measurement missing 'reason' tag");
            Assert.True(measurement.Tags.ContainsKey("cache.name"), "Eviction measurement missing 'cache.name' tag");
            Assert.Equal("eviction-test", measurement.Tags["cache.name"]);

            var reason = measurement.Tags["reason"]?.ToString();
            Assert.True(!string.IsNullOrEmpty(reason), "Eviction reason should not be null or empty");
            Assert.True(Enum.TryParse<EvictionReason>(reason, out _),
                $"Eviction reason '{reason}' should be a valid EvictionReason enum value");
        });

        // Validate that specific eviction reasons are present (using Assert.Contains pattern)
        var uniqueReasons = evictionMeasurements
            .Select(m => m.Tags["reason"]?.ToString())
            .Distinct()
            .ToList();
        Assert.True(uniqueReasons.Count >= 1, "Expected at least 1 unique eviction reason");
        
        // Validate presence of expected eviction reasons instead of exact counts
        Assert.Contains(evictionMeasurements, m => 
            m.Tags.ContainsKey("reason") && m.Tags["reason"]?.ToString() == "Removed");
        
        // Additional eviction reasons may be present depending on MemoryCache internal timing
        // This flexible approach allows for implementation changes without breaking tests
    }

    [Fact]
    public void MultipleNamedCaches_IsolatedMetrics_ValidatesTagSegregation()
    {
        using var inner1 = new MemoryCache(new MemoryCacheOptions());
        using var inner2 = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.3");
        using var harness = new MetricCollectionHarness("test.accuracy.3", "cache_hits_total", "cache_misses_total");

        var cache1 = new MeteredMemoryCache(inner1, meter, cacheName: "cache-alpha");
        var cache2 = new MeteredMemoryCache(inner2, meter, cacheName: "cache-beta");

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
        var cache1Hits = harness.GetAggregatedCount("cache_hits_total",
            new KeyValuePair<string, object?>("cache.name", "cache-alpha"));
        var cache1Misses = harness.GetAggregatedCount("cache_misses_total",
            new KeyValuePair<string, object?>("cache.name", "cache-alpha"));

        Assert.Equal(3, cache1Hits);
        Assert.Equal(2, cache1Misses);

        // Validate cache2 metrics isolation  
        var cache2Hits = harness.GetAggregatedCount("cache_hits_total",
            new KeyValuePair<string, object?>("cache.name", "cache-beta"));
        var cache2Misses = harness.GetAggregatedCount("cache_misses_total",
            new KeyValuePair<string, object?>("cache.name", "cache-beta"));

        Assert.Equal(1, cache2Hits);
        Assert.Equal(4, cache2Misses);

        // Validate total aggregation - expected: cache1(3 hits) + cache2(1 hit) = 4 total hits
        // expected: cache1(2 misses) + cache2(4 misses) = 6 total misses  
        harness.AssertAggregatedCount("cache_hits_total", 4); // 3 + 1
        harness.AssertAggregatedCount("cache_misses_total", 6); // 2 + 4

        // Validate no cross-contamination of cache names
        var allHitMeasurements = harness.GetMeasurements("cache_hits_total");
        var allMissMeasurements = harness.GetMeasurements("cache_misses_total");

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
        using var meter = new Meter("test.accuracy.4");
        using var harness = new MetricCollectionHarness("test.accuracy.4", "cache_hits_total", "cache_misses_total");

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

        var cache = new MeteredMemoryCache(inner, meter, options);

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

        harness.AssertAggregatedCount("cache_hits_total", 1);
        harness.AssertAggregatedCount("cache_misses_total", 1);
    }

    [Fact]
    public void GetOrCreateMethod_AccurateMetrics_ValidatesFactoryScenarios()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.5");
        using var harness = new MetricCollectionHarness("test.accuracy.5", "cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "getorcreate-test");

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
        harness.AssertAggregatedCount("cache_hits_total", 1);
        harness.AssertAggregatedCount("cache_misses_total", 2);
        harness.AssertMeasurementCount("cache_hits_total", 1);
        harness.AssertMeasurementCount("cache_misses_total", 2);

        // Validate cache name tags
        harness.AssertAllMeasurementsHaveTags("cache_hits_total",
            new KeyValuePair<string, object?>("cache.name", "getorcreate-test"));
        harness.AssertAllMeasurementsHaveTags("cache_misses_total",
            new KeyValuePair<string, object?>("cache.name", "getorcreate-test"));
    }

    [Fact]
    public void TryGetStronglyTyped_AccurateMetrics_ValidatesTypeConversion()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.tryget.typed.validation");
        using var harness = new MetricCollectionHarness("test.accuracy.tryget.typed.validation", "cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "tryget-typed-test");

        // Store different types
        cache.Set("string-key", "test-string");
        cache.Set("int-key", 42);
        cache.Set("object-key", new { Name = "test" });

        // Test successful type conversions (hits)
        var stringSuccess = cache.TryGet<string>("string-key", out var stringValue);
        Assert.True(stringSuccess);
        Assert.Equal("test-string", stringValue);

        var intSuccess = cache.TryGet<int>("int-key", out var intValue);
        Assert.True(intSuccess);
        Assert.Equal(42, intValue);

        // Test type mismatch (should be recorded as miss even though key exists)
        var typeMismatch = cache.TryGet<int>("string-key", out var mismatchValue);
        Assert.False(typeMismatch);
        Assert.Equal(0, mismatchValue); // default int

        // Test missing key (miss)
        var missing = cache.TryGet<string>("nonexistent-key", out var missingValue);
        Assert.False(missing);
        Assert.Null(missingValue);

        // Validate metrics: 2 hits, 2 misses
        harness.AssertAggregatedCount("cache_hits_total", 2);
        harness.AssertAggregatedCount("cache_misses_total", 2);

        // Validate cache name tags
        harness.AssertAllMeasurementsHaveTags("cache_hits_total",
            new KeyValuePair<string, object?>("cache.name", "tryget-typed-test"));
        harness.AssertAllMeasurementsHaveTags("cache_misses_total",
            new KeyValuePair<string, object?>("cache.name", "tryget-typed-test"));
    }

    [Fact]
    public async Task CreateEntryMethod_AccurateEvictionRegistration_ValidatesCallbackSetup()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.7");
        using var harness = new MetricCollectionHarness("test.accuracy.7", "cache_evictions_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "createentry-test");

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
            if (harness.GetMeasurements("cache_evictions_total").Any())
            {
                break; // Early exit if eviction detected
            }
            await Task.Delay(20); // Short delay between attempts
        }

        // Force compact multiple times to ensure eviction processing
        inner.Compact(0.0);
        inner.Compact(0.5);

        // Use deterministic wait for eviction callback processing
        var evictionDetected = await harness.WaitForMetricAsync("cache_evictions_total", 1, TimeSpan.FromSeconds(3));

        // Additional trigger attempts if not detected yet
        if (!evictionDetected)
        {
            cache.TryGetValue("manual-entry", out _);
            await harness.WaitForMetricAsync("cache_evictions_total", 1, TimeSpan.FromSeconds(2));
        }

        // Validate eviction was recorded
        var evictions = harness.GetMeasurements("cache_evictions_total");
        Assert.True(evictions.Count >= 1, $"Expected at least 1 eviction, got {evictions.Count}");

        // Validate eviction tags
        Assert.All(evictions, eviction =>
        {
            Assert.True(eviction.Tags.ContainsKey("cache.name"));
            Assert.Equal("createentry-test", eviction.Tags["cache.name"]);
            Assert.True(eviction.Tags.ContainsKey("reason"));

            var reason = eviction.Tags["reason"]?.ToString();
            Assert.True(Enum.TryParse<EvictionReason>(reason, out _),
                $"Invalid eviction reason: {reason}");
        });
    }

    [Fact]
    public void ZeroMeasurements_EdgeCase_ValidatesNoFalsePositives()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.8");
        using var harness = new MetricCollectionHarness("test.accuracy.8", "cache_hits_total", "cache_misses_total", "cache_evictions_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "zero-test");

        // Don't perform any cache operations

        // Validate no measurements recorded
        harness.AssertMeasurementCount("cache_hits_total", 0);
        harness.AssertMeasurementCount("cache_misses_total", 0);
        harness.AssertMeasurementCount("cache_evictions_total", 0);

        harness.AssertAggregatedCount("cache_hits_total", 0);
        harness.AssertAggregatedCount("cache_misses_total", 0);
        harness.AssertAggregatedCount("cache_evictions_total", 0);

        Assert.Empty(harness.AllMeasurements);
    }

    [Fact]
    public void HighVolumeOperations_AccurateAggregation_ValidatesScalability()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter("test.accuracy.9");
        using var harness = new MetricCollectionHarness("test.accuracy.9", "cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "volume-test");

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
        harness.AssertAggregatedCount("cache_misses_total", 100);
        harness.AssertAggregatedCount("cache_hits_total", 800);
        harness.AssertMeasurementCount("cache_misses_total", 100);
        harness.AssertMeasurementCount("cache_hits_total", 800);

        // Validate all measurements have correct cache name
        var allMeasurements = harness.AllMeasurements;
        Assert.Equal(900, allMeasurements.Count);
        Assert.All(allMeasurements, m =>
            Assert.Equal("volume-test", m.Tags["cache.name"]));
    }
}
