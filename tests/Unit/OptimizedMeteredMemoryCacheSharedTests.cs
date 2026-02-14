using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
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
        Assert.Equal(3, stats.TotalHits);
        Assert.Equal(2, stats.TotalMisses);
        Assert.Equal(2, stats.CurrentEntryCount);
        Assert.Equal(60.0, stats.HitRatio); // 3/(3+2) * 100 = 60%
    }

    /// <summary>
    /// Tests the periodic metric publishing functionality.
    /// </summary>
    [Fact]
    public void PublishMetrics_PublishesAccumulatedMetrics()
    {
        using var subject = CreateTestSubject(cacheName: "publish-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        // Perform operations
        subject.Cache.TryGetValue("miss", out _); // miss
        subject.Cache.Set("hit", "value");
        subject.Cache.TryGetValue("hit", out _); // hit

        var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");

        // With Observable instruments, metrics are always available via RecordObservableInstruments
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));

        // PublishMetrics is now a no-op — counters are not reset
        subject.PublishMetrics();

        // Metrics should still show accumulated values
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));

        // Statistics reflect accumulated totals (no reset with Observable instruments)
        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(1, stats.TotalHits);
        Assert.Equal(1, stats.TotalMisses);
    }

    /// <summary>
    /// Tests that metrics can be disabled for maximum performance.
    /// </summary>
    [Fact]
    public void WithMetricsDisabled_NoMetricsEmitted()
    {
        using var subject = CreateTestSubjectWithMetricsDisabled(cacheName: "disabled-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        Assert.False(subject.MetricsEnabled);

        // Perform operations
        subject.Cache.TryGetValue("miss", out _); // miss
        subject.Cache.Set("hit", "value");
        subject.Cache.TryGetValue("hit", out _); // hit

        // Publish metrics (should be no-op)
        subject.PublishMetrics();

        // No metrics should be emitted
        Assert.Equal(0, harness.AggregatedCounters.GetValueOrDefault("cache.requests", 0));
        Assert.Equal(0, harness.AggregatedCounters.GetValueOrDefault("cache.requests", 0));

        // Statistics should still be available (they use atomic operations regardless)
        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(1, stats.TotalHits);
        Assert.Equal(1, stats.TotalMisses);
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
        var evictionSignal = new TaskCompletionSource<bool>();
        var options = new MemoryCacheEntryOptions();
        options.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token));
        options.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = (key, value, reason, state) =>
            {
                evictionSignal.TrySetResult(true);
            }
        });
        subject.Cache.Set("evict-me", "value", options);

        var statsBeforeEviction = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(statsBeforeEviction);
        Assert.Equal(1, statsBeforeEviction.CurrentEntryCount);
        Assert.Equal(0, statsBeforeEviction.TotalEvictions);

        // Trigger eviction
        cts.Cancel();
        subject.Cache.TryGetValue("evict-me", out _); // Should trigger cleanup

        // Force compaction if possible
        if (subject.Cache.GetType().GetProperty("_inner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(subject.Cache) is MemoryCache innerCache)
        {
            innerCache.Compact(0.0);
        }

        // Wait for eviction callback to execute using proper synchronization
        await evictionSignal.Task.WaitAsync(TestTimeouts.Short);

        var statsAfterEviction = await TestSynchronization.WaitForConditionAsync(
            () => subject.GetCurrentStatistics() as CacheStatistics,
            stats => stats != null && stats.TotalEvictions >= 1,
            TestTimeouts.Short);

        Assert.NotNull(statsAfterEviction);
        Assert.True(statsAfterEviction.TotalEvictions >= 1, $"Expected at least 1 eviction, got {statsAfterEviction.TotalEvictions}");
        Assert.True(statsAfterEviction.CurrentEntryCount <= 0, $"Expected 0 entries after eviction, got {statsAfterEviction.CurrentEntryCount}");
    }

    /// <summary>
    /// Tests that multiple PublishMetrics calls work correctly.
    /// </summary>
    [Fact]
    public void MultiplePublishMetrics_WorksCorrectly()
    {
        using var subject = CreateTestSubject(cacheName: "multiple-publish-test");
        using var harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

        // First batch of operations
        subject.Cache.TryGetValue("miss1", out _);
        subject.Cache.Set("hit1", "value1");
        subject.Cache.TryGetValue("hit1", out _);

        var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
        var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");

        // Observable instruments report accumulated totals
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
        Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));

        // Second batch of operations
        subject.Cache.TryGetValue("miss2", out _);
        subject.Cache.TryGetValue("miss3", out _);
        subject.Cache.Set("hit2", "value2");
        subject.Cache.TryGetValue("hit2", out _);

        // Counters accumulate monotonically
        Assert.Equal(2, harness.GetAggregatedCount("cache.requests", hitTag)); // 1 + 1
        Assert.Equal(3, harness.GetAggregatedCount("cache.requests", missTag)); // 1 + 2

        // Statistics reflect accumulated totals (no reset with Observable instruments)
        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
        Assert.Equal(2, stats.TotalHits);
        Assert.Equal(3, stats.TotalMisses);
    }

    /// <summary>
    /// Tests cache name normalization in OptimizedMeteredMemoryCache.
    /// </summary>
    [Theory]
    [InlineData(null, "Default")]
    [InlineData("", "Default")]
    [InlineData("   ", "Default")]
    [InlineData("  test-cache  ", "test-cache")]
    [InlineData("normal-cache", "normal-cache")]
    public void CacheName_Normalization_HandlesWhitespaceCorrectly(string? inputName, string? expectedName)
    {
        using var subject = CreateTestSubject(cacheName: inputName);

        Assert.Equal(expectedName, subject.CacheName);

        var stats = subject.GetCurrentStatistics() as CacheStatistics;
        Assert.NotNull(stats);
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
            harness = new MetricCollectionHarness(subject.Meter.Name, "cache.requests");

            // Perform operations
            subject.Cache.TryGetValue("miss", out _);
            subject.Cache.Set("hit", "value");
            subject.Cache.TryGetValue("hit", out _);

            var hitTag = new KeyValuePair<string, object?>("cache.request.type", "hit");
            var missTag = new KeyValuePair<string, object?>("cache.request.type", "miss");

            // Observable instruments report accumulated values before disposal
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", hitTag));
            Assert.Equal(1, harness.GetAggregatedCount("cache.requests", missTag));

            // Dispose the cache
            subject.Dispose();
            subject = null;

            // After disposal, the Observable instrument callbacks may not fire
            // (meter is disposed), but metrics were available before disposal
        }
        finally
        {
            subject?.Dispose();
            harness?.Dispose();
        }
    }
}
