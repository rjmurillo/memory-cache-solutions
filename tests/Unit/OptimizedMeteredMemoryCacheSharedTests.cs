using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Unit.Shared;

namespace Unit;

/// <summary>
/// Shared tests for OptimizedMeteredMemoryCache using the common test framework.
/// These tests run the same scenarios as MeteredMemoryCache plus additional OptimizedMeteredMemoryCache-specific features.
/// </summary>
public class OptimizedMeteredMemoryCacheSharedTests : MeteredCacheTestBase<OptimizedMeteredCacheTestSubject>
{
    /// <summary>
    /// Creates a test subject for the OptimizedMeteredMemoryCache implementation.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="cacheName">Optional logical name for the cache instance.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when the test subject is disposed.</param>
    /// <returns>A new <see cref="OptimizedMeteredCacheTestSubject"/> instance for testing the optimized cache implementation with metrics enabled.</returns>
    protected override OptimizedMeteredCacheTestSubject CreateTestSubject(
        IMemoryCache? innerCache = null,
        Meter? meter = null,
        string? cacheName = null,
        bool disposeInner = true)
    {
        return new OptimizedMeteredCacheTestSubject(innerCache, meter, cacheName, disposeInner, enableMetrics: true);
    }

    /// <summary>
    /// Creates a test subject with metrics disabled for performance testing.
    /// </summary>
    private OptimizedMeteredCacheTestSubject CreateTestSubjectWithMetricsDisabled(
        IMemoryCache? innerCache = null,
        Meter? meter = null,
        string? cacheName = null,
        bool disposeInner = true)
    {
        return new OptimizedMeteredCacheTestSubject(innerCache, meter, cacheName, disposeInner, enableMetrics: false);
    }

    /// <summary>
    /// Tests the GetCurrentStatistics method specific to OptimizedMeteredMemoryCache.
    /// </summary>
    [Fact]
    public void GetCurrentStatistics_ReturnsAccurateStatistics()
    {
        using var subject = CreateTestSubject(cacheName: "stats-test");

        // Perform cache operations
        subject.Cache.TryGetValue("miss1", out _); // miss
        subject.Cache.TryGetValue("miss2", out _); // miss
        subject.Cache.Set("hit1", "value1");
        subject.Cache.Set("hit2", "value2");
        subject.Cache.TryGetValue("hit1", out _); // hit
        subject.Cache.TryGetValue("hit2", out _); // hit
        subject.Cache.TryGetValue("hit1", out _); // hit

        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(3, stats.HitCount);
        Assert.Equal(2, stats.MissCount);
        Assert.Equal(2, stats.CurrentEntryCount);
        Assert.Equal("stats-test", stats.CacheName);
        Assert.Equal(60.0, stats.HitRatio); // 3/(3+2) * 100 = 60%
    }

    /// <summary>
    /// Tests the periodic metric publishing functionality.
    /// </summary>
    [Fact]
    public void PublishMetrics_PublishesAccumulatedMetrics()
    {
        using var subject = CreateTestSubject(cacheName: "publish-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache_hits_total", "cache_misses_total");

        // Perform operations (metrics accumulated but not yet published)
        subject.Cache.TryGetValue("miss", out _); // miss
        subject.Cache.Set("hit", "value");
        subject.Cache.TryGetValue("hit", out _); // hit

        // Initially no metrics should be published (they're accumulated)
        Assert.Equal(0, harness.AggregatedCounters.GetValueOrDefault("cache_hits_total", 0));
        Assert.Equal(0, harness.AggregatedCounters.GetValueOrDefault("cache_misses_total", 0));

        // Publish accumulated metrics
        subject.PublishMetrics();

        // Now metrics should be published
        Assert.Equal(1, harness.AggregatedCounters["cache_hits_total"]);
        Assert.Equal(1, harness.AggregatedCounters["cache_misses_total"]);

        // After publishing, internal counters should be reset
        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(0, stats.HitCount);
        Assert.Equal(0, stats.MissCount);
    }

    /// <summary>
    /// Tests that metrics can be disabled for maximum performance.
    /// </summary>
    [Fact]
    public void WithMetricsDisabled_NoMetricsEmitted()
    {
        using var subject = CreateTestSubjectWithMetricsDisabled(cacheName: "disabled-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache_hits_total", "cache_misses_total");

        Assert.False(subject.MetricsEnabled);

        // Perform operations
        subject.Cache.TryGetValue("miss", out _); // miss
        subject.Cache.Set("hit", "value");
        subject.Cache.TryGetValue("hit", out _); // hit

        // Publish metrics (should be no-op)
        subject.PublishMetrics();

        // No metrics should be emitted
        Assert.Equal(0, harness.AggregatedCounters.GetValueOrDefault("cache_hits_total", 0));
        Assert.Equal(0, harness.AggregatedCounters.GetValueOrDefault("cache_misses_total", 0));

        // Statistics should still be available (they use atomic operations regardless)
        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(1, stats.HitCount);
        Assert.Equal(1, stats.MissCount);
        Assert.Equal("disabled-test", stats.CacheName);
    }

    /// <summary>
    /// Tests eviction tracking with GetCurrentStatistics.
    /// </summary>
    [Fact]
    public async Task EvictionTracking_UpdatesStatistics()
    {
        using var subject = CreateTestSubject(cacheName: "eviction-stats-test");

        // Create entry that will be evicted
        using var cts = new CancellationTokenSource();
        var options = new MemoryCacheEntryOptions();
        options.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token));
        subject.Cache.Set("evict-me", "value", options);

        var statsBeforeEviction = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(statsBeforeEviction);
        Assert.Equal(1, statsBeforeEviction.CurrentEntryCount);
        Assert.Equal(0, statsBeforeEviction.EvictionCount);

        // Trigger eviction
        cts.Cancel();
        subject.Cache.TryGetValue("evict-me", out _); // Should trigger cleanup

        // Force compaction if possible
        if (subject.Cache.GetType().GetProperty("_inner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(subject.Cache) is MemoryCache innerCache)
        {
            innerCache.Compact(0.0);
        }

        // Wait for eviction callback to execute using deterministic approach
        await Task.Yield();
        await Task.Yield();

        var statsAfterEviction = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(statsAfterEviction);
        Assert.True(statsAfterEviction.EvictionCount >= 1, $"Expected at least 1 eviction, got {statsAfterEviction.EvictionCount}");
        Assert.True(statsAfterEviction.CurrentEntryCount <= 0, $"Expected 0 entries after eviction, got {statsAfterEviction.CurrentEntryCount}");
    }

    /// <summary>
    /// Tests that multiple PublishMetrics calls work correctly.
    /// </summary>
    [Fact]
    public void MultiplePublishMetrics_WorksCorrectly()
    {
        using var subject = CreateTestSubject(cacheName: "multiple-publish-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache_hits_total", "cache_misses_total");

        // First batch of operations
        subject.Cache.TryGetValue("miss1", out _);
        subject.Cache.Set("hit1", "value1");
        subject.Cache.TryGetValue("hit1", out _);

        // First publish
        subject.PublishMetrics();
        Assert.Equal(1, harness.AggregatedCounters["cache_hits_total"]);
        Assert.Equal(1, harness.AggregatedCounters["cache_misses_total"]);

        // Second batch of operations
        subject.Cache.TryGetValue("miss2", out _);
        subject.Cache.TryGetValue("miss3", out _);
        subject.Cache.Set("hit2", "value2");
        subject.Cache.TryGetValue("hit2", out _);

        // Second publish (should add to previous totals)
        subject.PublishMetrics();
        Assert.Equal(2, harness.AggregatedCounters["cache_hits_total"]); // 1 + 1
        Assert.Equal(3, harness.AggregatedCounters["cache_misses_total"]); // 1 + 2

        // Statistics should be reset after each publish
        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(0, stats.HitCount);
        Assert.Equal(0, stats.MissCount);
    }

    /// <summary>
    /// Tests cache name normalization in OptimizedMeteredMemoryCache.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  test-cache  ", "test-cache")]
    [InlineData("normal-cache", "normal-cache")]
    public void CacheName_Normalization_HandlesWhitespaceCorrectly(string? inputName, string? expectedName)
    {
        using var subject = CreateTestSubject(cacheName: inputName);

        Assert.Equal(expectedName, subject.CacheName);

        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(expectedName, stats.CacheName);
    }

    /// <summary>
    /// Tests that OptimizedMeteredMemoryCache handles disposal correctly.
    /// </summary>
    [Fact]
    public void Dispose_PublishesRemainingMetricsAndDisposes()
    {
        OptimizedMeteredCacheTestSubject? subject = null;
        MetricCollectionHarness? harness = null;

        try
        {
            subject = CreateTestSubject(cacheName: "dispose-test");
            harness = new MetricCollectionHarness(subject.Meter.Name, "cache_hits_total", "cache_misses_total");

            // Perform operations
            subject.Cache.TryGetValue("miss", out _);
            subject.Cache.Set("hit", "value");
            subject.Cache.TryGetValue("hit", out _);

            // Dispose should publish remaining metrics automatically
            subject.Dispose();
            subject = null; // Prevent double disposal

            // Metrics should be published during disposal
            Assert.Equal(1, harness.AggregatedCounters.GetValueOrDefault("cache_hits_total", 0));
            Assert.Equal(1, harness.AggregatedCounters.GetValueOrDefault("cache_misses_total", 0));
        }
        finally
        {
            subject?.Dispose();
            harness?.Dispose();
        }
    }
}
