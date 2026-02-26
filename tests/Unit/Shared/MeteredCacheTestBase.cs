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
/// The class includes an OpenTelemetry-backed <see cref="MetricCollectionHarness"/> for capturing and
/// validating metrics via <see cref="OpenTelemetry.Exporter.InMemoryExporter{T}"/>, making it easy to verify that cache implementations
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
            // ALL measurements must have the correct cache.name tag
            harness.AssertAllMeasurementsHaveTags("cache.requests",
                new KeyValuePair<string, object?>("cache.name", "test-cache-name"));
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
            var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
            var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");
            var cache1Tag = new KeyValuePair<string, object?>("cache.name", "cache-one");
            var cache2Tag = new KeyValuePair<string, object?>("cache.name", "cache-two");

            // Exact counts per cache: each cache had 1 miss then 1 hit
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag, cache1Tag));
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag, cache1Tag));
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag, cache2Tag));
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag, cache2Tag));
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
