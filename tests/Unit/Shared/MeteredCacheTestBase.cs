using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Unit.Shared;

/// <summary>
/// Abstract base class for metered cache tests that provides common test scenarios and utilities.
/// Enables the DRY (Don't Repeat Yourself) principle by allowing the same test logic to be
/// executed against different cache implementations through the <see cref="IMeteredCacheTestSubject"/> interface.
/// </summary>
/// <typeparam name="TTestSubject">The concrete test subject type implementing <see cref="IMeteredCacheTestSubject"/>.</typeparam>
/// <remarks>
/// <para>
/// This base class provides a comprehensive testing framework for metered cache implementations,
/// including shared test scenarios, metric collection utilities, and validation helpers. It's
/// designed to be inherited by concrete test classes that test specific cache implementations.
/// </para>
/// <para>
/// The class includes a sophisticated <see cref="MetricCollectionHarness"/> for capturing and
/// validating OpenTelemetry metrics, making it easy to verify that cache implementations
/// correctly emit the expected metrics with proper tags and values.
/// </para>
/// <para>
/// This is particularly valuable for AI agents and developers who need to understand the
/// testing patterns, validation strategies, and expected behaviors of different cache
/// implementations in a standardized way.
/// </para>
/// </remarks>
public abstract class MeteredCacheTestBase<TTestSubject>
    where TTestSubject : IMeteredCacheTestSubject
{
    /// <summary>
    /// Enhanced metric collection harness that provides detailed validation capabilities
    /// for testing accurate metric emission from metered cache implementations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This harness captures OpenTelemetry metrics emitted by cache implementations and provides
    /// comprehensive validation utilities. It's thread-safe and designed to work with the
    /// .NET metrics system to capture counter measurements with their associated tags.
    /// </para>
    /// <para>
    /// The harness automatically aggregates counter values and provides filtering capabilities
    /// by instrument name and meter name, making it easy to validate specific metric emissions
    /// in complex test scenarios.
    /// </para>
    /// </remarks>
    protected sealed class MetricCollectionHarness : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<MetricMeasurement> _measurements = new();
        private readonly Dictionary<string, List<MetricMeasurement>> _measurementsByInstrument = new();
        private readonly Dictionary<string, long> _aggregatedCounters = new();
        private readonly string[] _instrumentNames;
        private readonly string? _meterNameFilter;
        private readonly object _lock = new object();

        /// <summary>
        /// Gets all captured metric measurements after taking a fresh snapshot.
        /// </summary>
        /// <value>A read-only list of all metric measurements captured by this harness.</value>
        public IReadOnlyList<MetricMeasurement> AllMeasurements
        {
            get
            {
                Collect();
                lock (_lock)
                {
                    return _measurements.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets aggregated counter values by instrument name after taking a fresh snapshot.
        /// </summary>
        /// <value>A read-only dictionary mapping instrument names to their aggregated counter values.</value>
        public IReadOnlyDictionary<string, long> AggregatedCounters
        {
            get
            {
                Collect();
                lock (_lock)
                {
                    return new Dictionary<string, long>(_aggregatedCounters);
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricCollectionHarness"/> class.
        /// </summary>
        /// <param name="instrumentNames">The names of the metric instruments to capture.</param>
        public MetricCollectionHarness(params string[] instrumentNames) : this(null, instrumentNames)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricCollectionHarness"/> class with meter filtering.
        /// </summary>
        /// <param name="meterNameFilter">Optional meter name filter to only capture metrics from specific meters.</param>
        /// <param name="instrumentNames">The names of the metric instruments to capture.</param>
        public MetricCollectionHarness(string? meterNameFilter, params string[] instrumentNames)
        {
            _instrumentNames = instrumentNames;
            _meterNameFilter = meterNameFilter;

            _listener.InstrumentPublished = (inst, listener) =>
            {
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
        /// Takes a fresh snapshot of all Observable instrument values.
        /// Clears previous measurements and records current absolute values.
        /// </summary>
        /// <remarks>
        /// Observable instruments (ObservableCounter, ObservableUpDownCounter, ObservableGauge) report
        /// absolute accumulated values, not deltas. This method clears accumulated data first, then
        /// records fresh values so that aggregation (ADD) produces correct totals.
        /// Safe to call multiple times — each call produces an idempotent snapshot.
        /// </remarks>
        public void Collect()
        {
            lock (_lock)
            {
                _measurements.Clear();
                _measurementsByInstrument.Clear();
                _aggregatedCounters.Clear();
            }
            _listener.RecordObservableInstruments();
        }

        /// <summary>
        /// Gets all measurements for a specific instrument name after taking a fresh snapshot.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
        /// <returns>A read-only list of measurements for the specified instrument.</returns>
        public IReadOnlyList<MetricMeasurement> GetMeasurements(string instrumentName)
        {
            Collect();
            lock (_lock)
            {
                return _measurementsByInstrument.GetValueOrDefault(instrumentName, new List<MetricMeasurement>()).ToArray();
            }
        }

        /// <summary>
        /// Gets measurements for a specific instrument that have all the required tags.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
        /// <param name="requiredTags">The tags that must be present in the measurements.</param>
        /// <returns>A read-only list of measurements that match the tag criteria.</returns>
        public IReadOnlyList<MetricMeasurement> GetMeasurementsWithTags(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
        {
            // GetMeasurements already calls Collect()
            return GetMeasurements(instrumentName)
                .Where(m => requiredTags.All(required =>
                    m.Tags.ContainsKey(required.Key) &&
                    Equals(m.Tags[required.Key], required.Value)))
                .ToList();
        }

        /// <summary>
        /// Gets the aggregated count for measurements matching specific tags.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
        /// <param name="requiredTags">The tags that must be present in the measurements.</param>
        /// <returns>The sum of all measurement values that match the tag criteria.</returns>
        public long GetAggregatedCount(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
        {
            // GetMeasurementsWithTags already calls Collect() via GetMeasurements
            return GetMeasurementsWithTags(instrumentName, requiredTags).Sum(m => m.Value);
        }

        /// <summary>
        /// Asserts that the aggregated value for an instrument matches the expected total.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to check.</param>
        /// <param name="expectedCount">The expected aggregated value.</param>
        /// <remarks>
        /// With Observable instruments, each instrument reports one measurement per tag set
        /// per collection. This asserts the total value, not the number of callback invocations.
        /// </remarks>
        public void AssertMeasurementCount(string instrumentName, int expectedCount)
        {
            // With Observable instruments, measurement count = number of unique tag sets,
            // not number of operations. Assert on the aggregated value instead.
            Collect();
            lock (_lock)
            {
                Assert.Equal(expectedCount, _aggregatedCounters.GetValueOrDefault(instrumentName, 0));
            }
        }

        /// <summary>
        /// Waits asynchronously for a metric to reach the expected value within the specified timeout.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to wait for.</param>
        /// <param name="expectedCount">The expected aggregated value.</param>
        /// <param name="timeout">The maximum time to wait for the metric.</param>
        /// <returns><see langword="true"/> if the metric reached the expected value; otherwise, <see langword="false"/>.</returns>
        public async Task<bool> WaitForMetricAsync(string instrumentName, int expectedCount, TimeSpan timeout)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                Collect();
                long currentValue;
                lock (_lock)
                {
                    currentValue = _aggregatedCounters.GetValueOrDefault(instrumentName, 0);
                }
                if (currentValue >= expectedCount)
                {
                    return true;
                }
                await Task.Yield();
            }
            return false;
        }

        /// <summary>
        /// Asserts that the aggregated counter value for an instrument matches the expected total.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to check.</param>
        /// <param name="expectedTotal">The expected aggregated counter value.</param>
        /// <exception cref="Xunit.Sdk.EqualException">Thrown when the actual total doesn't match the expected total.</exception>
        public void AssertAggregatedCount(string instrumentName, long expectedTotal)
        {
            Collect();
            lock (_lock)
            {
                Assert.Equal(expectedTotal, _aggregatedCounters.GetValueOrDefault(instrumentName, 0));
            }
        }

        /// <summary>
        /// Asserts that all measurements for an instrument have the required tags with matching values.
        /// </summary>
        /// <param name="instrumentName">The name of the instrument to check.</param>
        /// <param name="requiredTags">The tags that must be present in all measurements.</param>
        /// <exception cref="Xunit.Sdk.TrueException">Thrown when a measurement is missing a required tag.</exception>
        /// <exception cref="Xunit.Sdk.EqualException">Thrown when a tag value doesn't match the expected value.</exception>
        public void AssertAllMeasurementsHaveTags(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
        {
            // GetMeasurements already calls Collect()
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
        /// Resets the harness by clearing all captured measurements and aggregated counters.
        /// </summary>
        /// <remarks>
        /// This method is useful for reusing a harness across multiple test scenarios
        /// within the same test method.
        /// </remarks>
        public void Reset()
        {
            lock (_lock)
            {
                _measurements.Clear();
                _measurementsByInstrument.Clear();
                _aggregatedCounters.Clear();
            }
        }

        /// <summary>
        /// Disposes the harness and stops metric collection.
        /// </summary>
        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Represents a single metric measurement with all associated metadata.
    /// </summary>
    protected sealed record MetricMeasurement(
        string InstrumentName,
        long Value,
        IReadOnlyDictionary<string, object?> Tags,
        DateTime Timestamp);

    /// <summary>
    /// Creates a test subject for the specific implementation being tested.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="cacheName">Optional logical name for the cache instance.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when the test subject is disposed.</param>
    /// <returns>A new test subject instance for the specific cache implementation.</returns>
    /// <remarks>
    /// This method must be implemented by concrete test classes to create instances
    /// of their specific cache implementation wrapped in the appropriate test subject.
    /// </remarks>
    protected abstract TTestSubject CreateTestSubject(
        IMemoryCache? innerCache = null,
        Meter? meter = null,
        string? cacheName = null,
        bool disposeInner = true);

    /// <summary>
    /// Creates a test subject with options for implementations that support it.
    /// Default implementation delegates to the basic CreateTestSubject method.
    /// </summary>
    protected virtual TTestSubject CreateTestSubjectWithOptions(
        IMemoryCache? innerCache,
        Meter? meter,
        object? options,
        bool disposeInner = true)
    {
        // Default implementation ignores options and creates basic test subject
        return CreateTestSubject(innerCache, meter, null, disposeInner);
    }

    #region Common Test Scenarios

    /// <summary>
    /// Tests basic hit and miss operations with exact metric counting.
    /// </summary>
    [Fact]
    public void HitAndMissOperations_RecordsCorrectMetrics()
    {
        using var subject = CreateTestSubject(cacheName: "test-cache");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        // Execute operations
        subject.Cache.TryGetValue("k", out _); // miss
        subject.Cache.Set("k", 10);            // set
        subject.Cache.TryGetValue("k", out _); // hit

        // Publish metrics if supported
        subject.PublishMetrics();

        var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");

        if (subject.MetricsEnabled)
        {
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));
        }
        else
        {
            Assert.Equal(0, harness.GetAggregatedCount("cache.requests", hitTag));
            Assert.Equal(0, harness.GetAggregatedCount("cache.requests", missTag));
        }
    }

    /// <summary>
    /// Tests eviction scenarios with metric validation.
    /// </summary>
    [Fact]
    public async Task EvictionScenario_RecordsEvictionMetrics()
    {
        using var subject = CreateTestSubject(cacheName: "eviction-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.evictions");

        using var cts = new CancellationTokenSource();
        var options = new MemoryCacheEntryOptions();
        options.AddExpirationToken(new CancellationChangeToken(cts.Token));

        subject.Cache.Set("k", 1, options);
        cts.Cancel();
        subject.Cache.TryGetValue("k", out _);

        // Force compaction on inner cache through reflection
        var innerCacheField = subject.Cache.GetType().GetField("_inner",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (innerCacheField?.GetValue(subject.Cache) is MemoryCache innerCache)
        {
            innerCache.Compact(0.0);
        }

        // Publish metrics if supported
        subject.PublishMetrics();

        if (subject.MetricsEnabled)
        {
            var evictionRecorded = await harness.WaitForMetricAsync("cache.evictions", 1, TimeSpan.FromSeconds(5));
            Assert.True(evictionRecorded, "Expected eviction to be recorded within timeout");
            Assert.True(harness.AggregatedCounters.TryGetValue("cache.evictions", out var ev) && ev >= 1);
        }
    }

    /// <summary>
    /// Tests cache name tag emission for named caches.
    /// </summary>
    [Fact]
    public void WithCacheName_EmitsCacheNameTag()
    {
        using var subject = CreateTestSubject(cacheName: "test-cache-name");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        subject.Cache.TryGetValue("k", out _); // miss
        subject.Cache.Set("k", 42);
        subject.Cache.TryGetValue("k", out _); // hit

        // Publish metrics if supported
        subject.PublishMetrics();

        if (subject.MetricsEnabled)
        {
            // At least one measurement should have the cache.name tag
            Assert.Contains(true, harness.AllMeasurements.Select(m =>
                m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "test-cache-name")));
        }
    }

    /// <summary>
    /// Tests multiple named caches emit separate metrics.
    /// </summary>
    [Fact]
    public void MultipleNamedCaches_EmitSeparateMetrics()
    {
        using var sharedMeter = new Meter(SharedUtilities.GetUniqueMeterName("test.shared"));
        using var subject1 = CreateTestSubject(meter: sharedMeter, cacheName: "cache-one");
        using var subject2 = CreateTestSubject(meter: sharedMeter, cacheName: "cache-two");
        using var harness = new MetricCollectionHarness(sharedMeter.Name, "cache.requests");
        // Generate metrics for both caches
        subject1.Cache.TryGetValue("key", out _); // miss for cache-one
        subject2.Cache.TryGetValue("key", out _); // miss for cache-two

        subject1.Cache.Set("key", "value1");
        subject2.Cache.Set("key", "value2");

        subject1.Cache.TryGetValue("key", out _); // hit for cache-one
        subject2.Cache.TryGetValue("key", out _); // hit for cache-two

        // Publish metrics if supported
        subject1.PublishMetrics();
        subject2.PublishMetrics();

        if (subject1.MetricsEnabled)
        {
            // Verify separate metrics for each cache
            var cache1Measurements = harness.AllMeasurements.Where(m =>
                m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "cache-one")).ToList();
            var cache2Measurements = harness.AllMeasurements.Where(m =>
                m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "cache-two")).ToList();

            Assert.NotEmpty(cache1Measurements);
            Assert.NotEmpty(cache2Measurements);
        }
    }

    /// <summary>
    /// Tests high volume operations for scalability validation.
    /// </summary>
    [Fact]
    public void HighVolumeOperations_AccurateAggregation()
    {
        using var subject = CreateTestSubject(cacheName: "volume-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        const int operationCount = 1000;

        // Generate high volume of mixed operations
        for (int i = 0; i < operationCount; i++)
        {
            var key = $"key-{i % 100}"; // Reuse keys to generate hits

            if (i < 100)
            {
                // First 100 operations will be misses (new keys)
                subject.Cache.TryGetValue(key, out _);
            }
            else
            {
                // Set values for first 100 keys
                if (i < 200)
                {
                    subject.Cache.Set(key, $"value-{i}");
                }
                else
                {
                    // Remaining operations will be hits (existing keys)
                    subject.Cache.TryGetValue(key, out _);
                }
            }
        }

        // Publish metrics if supported
        subject.PublishMetrics();

        var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");

        if (subject.MetricsEnabled)
        {
            // Validate exact counts: 100 misses + 800 hits = 900 total operations
            Assert.Equal(100, harness.GetAggregatedCount("cache.requests", missTag));
            Assert.Equal(800, harness.GetAggregatedCount("cache.requests", hitTag));
        }
    }

    #endregion
}
