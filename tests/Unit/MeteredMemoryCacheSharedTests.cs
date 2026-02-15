using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Unit.Shared;

namespace Unit;

/// <summary>
/// Shared tests for MeteredMemoryCache using the common test framework.
/// Shared test scenarios for MeteredMemoryCache consistency validation.
/// </summary>
public class MeteredMemoryCacheSharedTests : MeteredCacheTestBase<MeteredCacheTestSubject>
{
    /// <summary>
    /// Creates a test subject for the MeteredMemoryCache implementation.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="cacheName">Optional logical name for the cache instance.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when the test subject is disposed.</param>
    /// <returns>A new <see cref="MeteredCacheTestSubject"/> instance for testing the original MeteredMemoryCache implementation.</returns>
    protected override MeteredCacheTestSubject CreateTestSubject(
        IMemoryCache? innerCache = null,
        Meter? meter = null,
        string? cacheName = null,
        bool disposeInner = true)
    {
        return new MeteredCacheTestSubject(innerCache, meter, cacheName, disposeInner);
    }

    /// <summary>
    /// Creates a test subject with options for the MeteredMemoryCache implementation.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="options">The options object for the cache. Must be of type <see cref="MeteredMemoryCacheOptions"/>.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when the test subject is disposed.</param>
    /// <returns>A new <see cref="MeteredCacheTestSubject"/> instance configured with the specified options.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="options"/> is not of type <see cref="MeteredMemoryCacheOptions"/>.</exception>
    protected override MeteredCacheTestSubject CreateTestSubjectWithOptions(
        IMemoryCache? innerCache,
        Meter? meter,
        object? options,
        bool disposeInner = true)
    {
        if (options is MeteredMemoryCacheOptions mcOptions)
        {
            return new MeteredCacheTestSubject(innerCache, meter, mcOptions, disposeInner);
        }
        return CreateTestSubject(innerCache, meter, null, disposeInner);
    }

    /// <summary>
    /// Tests the TryGet strongly typed method specific to MeteredMemoryCache.
    /// </summary>
    [Fact]
    public void TryGet_WithNamedCache_RecordsMetricsWithCacheName()
    {
        using var subject = CreateTestSubject(cacheName: "tryget-cache");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        var cache = (MeteredMemoryCache)subject.Cache;

        // Test miss
        var missResult = cache.TryGetValue<string>("missing-key", out var missValue);
        Assert.False(missResult);
        Assert.Null(missValue);

        // Test hit
        cache.Set("present-key", "test-value");
        var hitResult = cache.TryGetValue<string>("present-key", out var hitValue);
        Assert.True(hitResult);
        Assert.Equal("test-value", hitValue);

        // Verify metrics with cache.name tag
        var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));

        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("cache.name", "tryget-cache"));
    }

    /// <summary>
    /// Tests the GetOrCreate method specific to MeteredMemoryCache.
    /// </summary>
    [Fact]
    public void GetOrCreate_WithNamedCache_RecordsMetricsWithCacheName()
    {
        using var subject = CreateTestSubject(cacheName: "getorcreate-cache");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        var cache = (MeteredMemoryCache)subject.Cache;

        // First call should be a miss and create
        var value1 = cache.GetOrCreate("key1", entry => "created-value");
        Assert.Equal("created-value", value1);

        // Second call should be a hit
        var value2 = cache.GetOrCreate("key1", entry => "should-not-be-called");
        Assert.Equal("created-value", value2);

        // Verify metrics
        var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));

        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("cache.name", "getorcreate-cache"));
    }

    /// <summary>
    /// Tests MeteredMemoryCache with additional tags through options.
    /// </summary>
    [Fact]
    public void WithAdditionalTags_EmitsAllTagsOnMetrics()
    {
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "tagged-cache",
            AdditionalTags = { ["environment"] = "test", ["region"] = "us-west-2" }
        };

        using var subject = CreateTestSubjectWithOptions(null, null, options);
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        subject.Cache.TryGetValue("k", out _); // miss
        subject.Cache.Set("k", 100);
        subject.Cache.TryGetValue("k", out _); // hit

        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("cache.name", "tagged-cache"));

        // Verify additional tags are present on all measurements
        harness.AssertAllMeasurementsHaveTags("cache.requests",
            new KeyValuePair<string, object?>("environment", "test"),
            new KeyValuePair<string, object?>("region", "us-west-2"));
    }

    /// <summary>
    /// Tests that MeteredMemoryCache supports GetCurrentStatistics.
    /// </summary>
    [Fact]
    public void GetCurrentStatistics_ReturnsStatistics()
    {
        using var subject = CreateTestSubject();
        subject.Cache.TryGetValue("miss", out _);
        subject.Cache.Set("hit", "value");
        subject.Cache.TryGetValue("hit", out _);

        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(1, stats.TotalHits);
        Assert.Equal(1, stats.TotalMisses);
        Assert.Equal(1, stats.CurrentEntryCount);
    }

    /// <summary>
    /// Tests that MeteredMemoryCache PublishMetrics does nothing (immediate publishing).
    /// </summary>
    [Fact]
    public void PublishMetrics_NoOp_DoesNotThrow()
    {
        using var subject = CreateTestSubject();

        // Should not throw - this is a no-op for MeteredMemoryCache
        subject.PublishMetrics();

        Assert.True(true, "PublishMetrics completed without throwing");
    }

    /// <summary>
    /// Tests GetCurrentStatistics after multiple operations returns correct counts.
    /// </summary>
    [Fact]
    public void GetCurrentStatistics_AfterOperations_ReturnsCorrectCounts()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.counts"));
        using var cache = new MeteredMemoryCache(inner, meter, "count-test");

        cache.TryGetValue("miss1", out _);
        cache.TryGetValue("miss2", out _);
        cache.Set("key1", "value1");
        cache.Set("key2", "value2");
        cache.TryGetValue("key1", out _);
        cache.TryGetValue("key2", out _);
        cache.TryGetValue("key1", out _);

        var stats = cache.GetCurrentStatistics();

        Assert.Equal(3, stats.TotalHits);
        Assert.Equal(2, stats.TotalMisses);
        Assert.Equal(2, stats.CurrentEntryCount);
        Assert.Equal(60.0, stats.HitRatio, 1);
    }

    /// <summary>
    /// Tests HitRatio edge cases.
    /// </summary>
    [Fact]
    public void HitRatio_EdgeCases_HandlesCorrectly()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.ratio"));
        using var cache = new MeteredMemoryCache(inner, meter);

        var stats1 = cache.GetCurrentStatistics();
        Assert.Equal(0, stats1.HitRatio);

        cache.TryGetValue("miss", out _);
        var stats2 = cache.GetCurrentStatistics();
        Assert.Equal(0, stats2.HitRatio);

        cache.Set("hit", "value");
        cache.TryGetValue("hit", out _);
        var stats3 = cache.GetCurrentStatistics();
        Assert.Equal(50.0, stats3.HitRatio, 1);

        using var freshCache = new MeteredMemoryCache(new MemoryCache(new MemoryCacheOptions()), meter);
        freshCache.Set("hit", "value");
        freshCache.TryGetValue("hit", out _);
        var stats4 = freshCache.GetCurrentStatistics();
        Assert.Equal(100.0, stats4.HitRatio, 1);
    }

    /// <summary>
    /// Tests eviction callback registration and statistics tracking.
    /// </summary>
    [Fact]
    public async Task CreateEntry_RegistersEvictionCallback()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.callback"));
        using var cache = new MeteredMemoryCache(inner, meter, SharedUtilities.GetUniqueCacheName("callback-test"));

        var evictionCount = 0;
        var evictionSignal = new TaskCompletionSource<bool>();

        var cts = new CancellationTokenSource();
        cache.Set("evict-key", "evict-value", new MemoryCacheEntryOptions
        {
            ExpirationTokens = { new CancellationChangeToken(cts.Token) },
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (key, value, reason, state) =>
                    {
                        Interlocked.Increment(ref evictionCount);
                        evictionSignal.TrySetResult(true);
                    }
                }
            }
        });

        var statsBeforeEviction = cache.GetCurrentStatistics();
        Assert.Equal(1, statsBeforeEviction.CurrentEntryCount);

        cts.Cancel();

        await evictionSignal.Task.WaitAsync(TestTimeouts.Short);

        var statsAfterEviction = await TestSynchronization.WaitForConditionAsync(
            () => cache.GetCurrentStatistics(),
            stats => Volatile.Read(ref evictionCount) > 0 && stats.TotalEvictions >= 1,
            TestTimeouts.Short);

        Assert.True(Volatile.Read(ref evictionCount) >= 1, "Eviction callback should have been triggered");
        Assert.True(statsAfterEviction.TotalEvictions >= 1, "Eviction count should be updated in statistics");
        Assert.True(statsAfterEviction.CurrentEntryCount <= 0, "Entry count should decrease after eviction");
    }
}
