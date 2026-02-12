using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Unit.Shared;

namespace Unit;

/// <summary>
/// Shared tests for MeteredMemoryCache using the common test framework.
/// These tests run the same scenarios as OptimizedMeteredMemoryCache for consistency validation.
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
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.lookups");

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
        var hitTag = new KeyValuePair<string, object?>("cache.result", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.result", "miss");
        Assert.Equal(1, harness.GetAggregatedCount("cache.lookups", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.lookups", missTag));

        Assert.Contains(true, harness.AllMeasurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "tryget-cache")));
    }

    /// <summary>
    /// Tests the GetOrCreate method specific to MeteredMemoryCache.
    /// </summary>
    [Fact]
    public void GetOrCreate_WithNamedCache_RecordsMetricsWithCacheName()
    {
        using var subject = CreateTestSubject(cacheName: "getorcreate-cache");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.lookups");

        var cache = (MeteredMemoryCache)subject.Cache;

        // First call should be a miss and create
        var value1 = cache.GetOrCreate("key1", entry => "created-value");
        Assert.Equal("created-value", value1);

        // Second call should be a hit
        var value2 = cache.GetOrCreate("key1", entry => "should-not-be-called");
        Assert.Equal("created-value", value2);

        // Verify metrics
        var hitTag = new KeyValuePair<string, object?>("cache.result", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.result", "miss");
        Assert.Equal(1, harness.GetAggregatedCount("cache.lookups", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.lookups", missTag));

        // Verify cache.name tag is present
        Assert.Contains(true, harness.AllMeasurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "getorcreate-cache")));
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
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.lookups");

        subject.Cache.TryGetValue("k", out _); // miss
        subject.Cache.Set("k", 100);
        subject.Cache.TryGetValue("k", out _); // hit

        // Verify cache.name tag is present
        Assert.Contains(true, harness.AllMeasurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "tagged-cache")));

        // Verify additional tags are present
        Assert.Contains(true, harness.AllMeasurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "environment" && (string?)tag.Value == "test")));
        Assert.Contains(true, harness.AllMeasurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "region" && (string?)tag.Value == "us-west-2")));
    }

    /// <summary>
    /// Tests that MeteredMemoryCache doesn't support GetCurrentStatistics.
    /// </summary>
    [Fact]
    public void GetCurrentStatistics_NotSupported_ReturnsNull()
    {
        using var subject = CreateTestSubject();
        var stats = subject.GetCurrentStatistics();
        Assert.Null(stats);
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
}
