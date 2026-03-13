using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

using Unit.Shared;

namespace Unit;

/// <summary>
/// BCL adoption coverage tests — fills every documented behavior gap identified
/// in the pre-adoption audit of <see cref="MeteredMemoryCache"/>.
/// Each test is annotated with the behavior ID from the coverage matrix.
/// </summary>
public class BclAdoptionCoverageTests
{
    #region 1a — MeterName constant value

    [Fact]
    public void MeterName_Constant_HasExpectedValue()
    {
        Assert.Equal("Microsoft.Extensions.Caching.Memory.MemoryCache", MeteredMemoryCache.MeterName);
    }

    #endregion

    #region 3b — Eviction increments on Expired (AbsoluteExpiration)

    [Fact]
    public async Task Eviction_OnExpired_IncrementsEvictionCounter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.3b"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.evictions");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("expired"));

        // Add entry with AbsoluteExpirationRelativeToNow in the past (expires immediately)
        using (var entry = cache.CreateEntry("expired-key"))
        {
            entry.Value = "value";
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1);
        }

        // Wait for expiration to be detectable
        await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        // Access the expired key to trigger eviction processing
        cache.TryGetValue("expired-key", out _);
        inner.Compact(0.0);

        var reached = await harness.WaitForMetricAsync("cache.evictions", 1, TimeSpan.FromSeconds(5));
        Assert.True(reached, "Expected eviction count >= 1 for Expired eviction within timeout");
    }

    #endregion

    #region 3c — Eviction increments on Capacity eviction

    [Fact]
    public async Task Eviction_OnCapacity_IncrementsEvictionCounter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 2,
        });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.3c"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.evictions");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("capacity"));

        // Fill to capacity
        using (var e1 = cache.CreateEntry("key1")) { e1.Value = "v1"; e1.Size = 1; }
        using (var e2 = cache.CreateEntry("key2")) { e2.Value = "v2"; e2.Size = 1; }

        // This should trigger capacity eviction of at least one entry
        using (var e3 = cache.CreateEntry("key3")) { e3.Value = "v3"; e3.Size = 1; }

        // Compact to force eviction processing
        inner.Compact(0.5);

        var reached = await harness.WaitForMetricAsync("cache.evictions", 1, TimeSpan.FromSeconds(5));
        Assert.True(reached, "Expected eviction count >= 1 for Capacity eviction within timeout");
    }

    #endregion

    #region 3e — Set overwrite (Replaced) does NOT increment eviction counter

    [Fact]
    public void Eviction_OnReplaced_DoesNotIncrementEvictionCounter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.3e"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.evictions");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("replaced"));

        // Set initial value
        cache.Set("key", "original");

        // Overwrite with new value (triggers Replaced eviction reason internally)
        cache.Set("key", "updated");

        harness.Collect();
        var evictions = harness.GetAggregatedCount("cache.evictions");
        Assert.Equal(0, evictions);
    }

    #endregion

    #region 4a — Entry count increments on commit (Dispose), not on CreateEntry

    [Fact]
    public void EntryCount_IncrementsOnCommit_NotOnCreate()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.4a"));
        using var harness = new MetricCollectionHarness(meter.Name, "cache.entries");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("commit"));

        // Create entry but do NOT dispose (commit) it
        var entry = cache.CreateEntry("uncommitted-key");
        entry.Value = "value";

        // Entry count should still be 0 because entry hasn't been committed
        harness.Collect();
        var countBeforeCommit = harness.GetAggregatedCount("cache.entries");
        Assert.Equal(0, countBeforeCommit);

        // Now commit by disposing
        entry.Dispose();

        harness.Collect();
        var countAfterCommit = harness.GetAggregatedCount("cache.entries");
        Assert.Equal(1, countAfterCommit);
    }

    #endregion

    #region 4b — Entry count decrements on Remove and Replaced

    [Fact]
    public async Task EntryCount_DecrementsOnRemove()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.4b.remove"));

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("remove"));

        cache.Set("key1", "value1");
        cache.Set("key2", "value2");
        Assert.Equal(2, cache.GetCurrentStatistics().CurrentEntryCount);

        // Explicit Remove — eviction callback fires asynchronously
        cache.Remove("key1");

        // Poll until the entry count reflects the removal (callback is async in MemoryCache)
        var success = await PollUntilAsync(
            () => cache.GetCurrentStatistics().CurrentEntryCount == 1,
            TimeSpan.FromSeconds(5));
        Assert.True(success, $"Expected entry count 1 after Remove, got {cache.GetCurrentStatistics().CurrentEntryCount}");
    }

    [Fact]
    public async Task EntryCount_DecrementsOnReplaced_NetCountStaysAccurate()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.4b.replaced"));

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("replaced"));

        cache.Set("key", "original");
        Assert.Equal(1, cache.GetCurrentStatistics().CurrentEntryCount);

        // Overwrite triggers Replaced (decrement) then new commit (increment)
        // Net count should remain 1 after async callback fires
        cache.Set("key", "updated");

        var success = await PollUntilAsync(
            () => cache.GetCurrentStatistics().CurrentEntryCount == 1,
            TimeSpan.FromSeconds(5));
        Assert.True(success, $"Expected entry count 1 after Replace, got {cache.GetCurrentStatistics().CurrentEntryCount}");
    }

    #endregion

    #region 5b — cache.estimated_size NOT registered when TrackStatistics=false

    [Fact]
    public void EstimatedSize_NotRegistered_WhenTrackStatisticsDisabled()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = false });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.5b"));

        var instrumentNames = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, _) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                instrumentNames.Add(inst.Name);
            }
        };
        listener.Start();

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("no-stats"));

        Assert.DoesNotContain("cache.estimated_size", instrumentNames);
        // Other instruments should still be registered
        Assert.Contains("cache.requests", instrumentNames);
        Assert.Contains("cache.evictions", instrumentNames);
        Assert.Contains("cache.entries", instrumentNames);
    }

    #endregion

    #region 5c — cache.estimated_size NOT registered when inner is not MemoryCache

    [Fact]
    public void EstimatedSize_NotRegistered_WhenInnerIsNotMemoryCache()
    {
        var mockCache = new FakeMemoryCache();
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.5c"));

        var instrumentNames = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, _) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                instrumentNames.Add(inst.Name);
            }
        };
        listener.Start();

        using var cache = new MeteredMemoryCache(mockCache, meter, cacheName: SharedUtilities.GetUniqueCacheName("fake"));

        Assert.DoesNotContain("cache.estimated_size", instrumentNames);
        Assert.Contains("cache.requests", instrumentNames);
    }

    #endregion

    #region 7c — Finalizer breaks circular reference when meterFactory is null

    [Fact]
    public void NullMeterFactory_CreatesOwnedMeter_DisposeCleansUp()
    {
        // When meterFactory is null, the cache creates an owned Meter (_ownedMeter != null).
        // Dispose should dispose the owned meter to break the circular reference.
        // We verify this by checking that the cache is functional before Dispose,
        // and that Dispose completes without error (owned meter is cleaned up).
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cache = new MeteredMemoryCache(inner, meterFactory: null, cacheName: SharedUtilities.GetUniqueCacheName("finalizer"));

        // Verify cache is functional (owned meter is working)
        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out var val));
        Assert.Equal("value", val);

        // Dispose should clean up the owned meter without throwing
        cache.Dispose();

        // After dispose, operations should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => cache.TryGetValue("key", out _));
    }

    #endregion

    #region 8b — GetCurrentStatistics().EstimatedSize from inner MemoryCache

    [Fact]
    public void GetCurrentStatistics_EstimatedSize_ComesFromInnerMemoryCache()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions
        {
            TrackStatistics = true,
            SizeLimit = 1000,
        });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.8b"));

        using var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("est-size"));

        // Add entry with known size
        using (var entry = cache.CreateEntry("sized-key"))
        {
            entry.Value = "data";
            entry.Size = 42;
        }

        var stats = cache.GetCurrentStatistics();
        Assert.NotNull(stats.EstimatedSize);
        Assert.Equal(42, stats.EstimatedSize.Value);

        // Add another entry
        using (var entry = cache.CreateEntry("sized-key2"))
        {
            entry.Value = "more-data";
            entry.Size = 58;
        }

        stats = cache.GetCurrentStatistics();
        Assert.Equal(100, stats.EstimatedSize!.Value);
    }

    [Fact]
    public void GetCurrentStatistics_EstimatedSize_NullWhenInnerIsNotMemoryCache()
    {
        var mockCache = new FakeMemoryCache();
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.8b.fake"));

        using var cache = new MeteredMemoryCache(mockCache, meter, cacheName: SharedUtilities.GetUniqueCacheName("no-size"));

        var stats = cache.GetCurrentStatistics();
        Assert.Null(stats.EstimatedSize);
    }

    #endregion

    #region 8c — GetCurrentStatistics throws ObjectDisposedException after Dispose

    [Fact]
    public void GetCurrentStatistics_AfterDispose_ThrowsObjectDisposedException()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.8c"));

        var cache = new MeteredMemoryCache(inner, meter, cacheName: SharedUtilities.GetUniqueCacheName("stats-disposed"));
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.GetCurrentStatistics());
    }

    #endregion

    #region 10b — cache.name is first tag in array

    [Fact]
    public void Tags_CacheNameIsFirstTag_InAllMeasurements()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("bcl.10b"));

        var capturedTags = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, _, tags, _) =>
        {
            var tagArray = new KeyValuePair<string, object?>[tags.Length];
            tags.CopyTo(tagArray.AsSpan());
            capturedTags.Add(tagArray);
        });
        listener.Start();

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = SharedUtilities.GetUniqueCacheName("tag-order"),
            AdditionalTags = new Dictionary<string, object?>
            {
                ["environment"] = "test",
                ["region"] = "us-east",
            },
        };
        using var cache = new MeteredMemoryCache(inner, meter, options);

        // Generate some metrics
        cache.Set("key", "value");
        cache.TryGetValue("key", out _);

        // Trigger Observable instrument callbacks
        listener.RecordObservableInstruments();

        Assert.NotEmpty(capturedTags);
        Assert.All(capturedTags, tagArray =>
        {
            Assert.True(tagArray.Length > 0, "Tag array should not be empty");
            Assert.Equal("cache.name", tagArray[0].Key);
        });
    }

    #endregion

    #region Helpers

    private static async Task<bool> PollUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        }

        return condition();
    }

    /// <summary>
    /// Minimal IMemoryCache stub that is NOT a MemoryCache instance.
    /// Used to test behaviors that depend on the inner cache type.
    /// </summary>
    private sealed class FakeMemoryCache : IMemoryCache
    {
        private readonly Dictionary<object, object?> _store = new();

        public ICacheEntry CreateEntry(object key) => new FakeCacheEntry(key, this);

        public void Remove(object key) => _store.Remove(key);

        public bool TryGetValue(object key, out object? value) => _store.TryGetValue(key, out value);

        public void Dispose() { }

        internal void Commit(object key, object? value) => _store[key] = value;

        private sealed class FakeCacheEntry : ICacheEntry
        {
            private readonly FakeMemoryCache _cache;

            public FakeCacheEntry(object key, FakeMemoryCache cache)
            {
                Key = key;
                _cache = cache;
            }

            public object Key { get; }
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = new List<PostEvictionCallbackRegistration>();
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }

            public void Dispose() => _cache.Commit(Key, Value);
        }
    }

    #endregion
}
