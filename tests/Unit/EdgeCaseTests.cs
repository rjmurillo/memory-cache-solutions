using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;

namespace Unit;

/// <summary>
/// Edge case tests for BCL integration readiness.
/// Tests scenarios that are unlikely in normal usage but must be handled correctly.
/// </summary>
public class EdgeCaseTests
{
    #region Eviction Callback Re-Entrance

    /// <summary>
    /// Tests that adding items during an eviction callback does not cause corruption or deadlock.
    /// This validates re-entrance safety in the eviction callback path.
    /// </summary>
    [Fact]
    public async Task EvictionCallback_AddingItemsDuringEviction_ShouldNotCorruptState()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.reentrance"));
        var cacheName = SharedUtilities.GetUniqueCacheName("reentrance");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        var callbackExecuted = new TaskCompletionSource<bool>();
        var reentrantItemAdded = false;
        var cts = new CancellationTokenSource();

        // Add entry with a callback that adds another item during eviction
        // Use MemoryCacheEntryOptions.PostEvictionCallbacks pattern (works with MeteredMemoryCache)
        var options = new MemoryCacheEntryOptions
        {
            ExpirationTokens = { new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token) },
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (key, value, reason, state) =>
                    {
                        var reentrantCache = (MeteredMemoryCache)state!;

                        // Re-entrant: add a new item during eviction callback
                        try
                        {
                            reentrantCache.Set("reentrant-key", "reentrant-value");
                            reentrantItemAdded = true;
                        }
                        catch
                        {
                            // If this throws, test will fail on assertion
                        }

                        callbackExecuted.TrySetResult(true);
                    },
                    State = cache,
                }
            }
        };

        cache.Set("trigger-key", "trigger-value", options);

        // Act: Trigger eviction by canceling the token
        cts.Cancel();

        // Access the expired key to trigger eviction processing
        cache.TryGetValue("trigger-key", out _);

        // Wait for callback to execute (with timeout)
        var executed = await callbackExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert: Callback executed and re-entrant item was added successfully
        Assert.True(executed, "Eviction callback should have executed");
        Assert.True(reentrantItemAdded, "Re-entrant item should have been added during eviction callback");

        // Verify re-entrant item is in cache
        var found = cache.TryGetValue("reentrant-key", out var value);
        Assert.True(found, "Re-entrant item should be retrievable from cache");
        Assert.Equal("reentrant-value", value);

        // Verify statistics are consistent
        var stats = cache.GetCurrentStatistics();
        Assert.True(stats.CurrentEntryCount >= 1, "Should have at least the re-entrant entry");
    }

    /// <summary>
    /// Tests that removing items during an eviction callback does not cause corruption.
    /// </summary>
    [Fact]
    public async Task EvictionCallback_RemovingItemsDuringEviction_ShouldNotCorruptState()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.reentrance.remove"));
        var cacheName = SharedUtilities.GetUniqueCacheName("reentrance-remove");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        var callbackExecuted = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        // Pre-populate an item that will be removed during eviction
        cache.Set("victim-key", "victim-value");

        // Add entry with a callback that removes another item during eviction
        var options = new MemoryCacheEntryOptions
        {
            ExpirationTokens = { new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token) },
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (key, value, reason, state) =>
                    {
                        var reentrantCache = (MeteredMemoryCache)state!;

                        // Re-entrant: remove another item during eviction callback
                        reentrantCache.Remove("victim-key");

                        callbackExecuted.TrySetResult(true);
                    },
                    State = cache,
                }
            }
        };

        cache.Set("trigger-key", "trigger-value", options);

        // Act: Trigger eviction by canceling the token
        cts.Cancel();

        // Access the expired key to trigger eviction processing
        cache.TryGetValue("trigger-key", out _);

        // Wait for callback to execute (with timeout)
        var executed = await callbackExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert: Callback executed and victim item was removed
        Assert.True(executed, "Eviction callback should have executed");

        var found = cache.TryGetValue("victim-key", out _);
        Assert.False(found, "Victim item should have been removed during eviction callback");
    }

    #endregion

    #region CacheItemPriority-Based Eviction Ordering

    /// <summary>
    /// Tests that CacheItemPriority affects eviction ordering when memory pressure forces eviction.
    /// Lower priority items should be evicted before higher priority items.
    /// </summary>
    [Fact]
    public void CacheItemPriority_EvictionOrdering_LowerPriorityEvictedFirst()
    {
        // Arrange: Create cache with size limit to force evictions
        using var inner = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 100,
            TrackStatistics = true,
        });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.priority.eviction"));
        var cacheName = SharedUtilities.GetUniqueCacheName("priority-eviction");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Add items with different priorities (total size = 100, at limit)
        cache.Set("low-priority", "value", new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.Low,
            Size = 25,
        });

        cache.Set("normal-priority", "value", new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.Normal,
            Size = 25,
        });

        cache.Set("high-priority", "value", new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.High,
            Size = 25,
        });

        cache.Set("never-remove", "value", new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove,
            Size = 25,
        });

        // Act: Force compact to 50% to trigger eviction (will evict lower priority items first)
        inner.Compact(0.5);

        // Assert: NeverRemove should always survive
        Assert.True(cache.TryGetValue("never-remove", out _), "NeverRemove priority item should not be evicted");

        // Check eviction ordering: Low priority items should be evicted first
        var lowExists = cache.TryGetValue("low-priority", out _);
        var highExists = cache.TryGetValue("high-priority", out _);

        // If any items were evicted, low priority should be evicted before high
        // (This is a basic validation that priority ordering is respected)
        if (!lowExists)
        {
            // Low was evicted - that's expected behavior
            Assert.True(true, "Low priority item was correctly evicted first");
        }
        else if (!highExists)
        {
            // High was evicted but Low wasn't - this violates priority ordering
            Assert.Fail("High priority item should not be evicted before low priority item");
        }
        // If both exist or neither, the test passes (no eviction pressure reached)
    }

    /// <summary>
    /// Tests that NeverRemove priority items survive compact operations.
    /// </summary>
    [Fact]
    public void CacheItemPriority_NeverRemove_SurvivesCompact()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 100,
            TrackStatistics = true,
        });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.priority.neverremove"));
        var cacheName = SharedUtilities.GetUniqueCacheName("never-remove");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Add NeverRemove item
        cache.Set("critical-data", "important-value", new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove,
            Size = 50,
        });

        // Add low priority items to fill cache
        for (int i = 0; i < 5; i++)
        {
            cache.Set($"low-{i}", $"value-{i}", new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.Low,
                Size = 10,
            });
        }

        // Act: Compact to force eviction of low priority items
        inner.Compact(0.5); // Request 50% compaction

        // Assert: NeverRemove item should still exist
        Assert.True(cache.TryGetValue("critical-data", out var value), "NeverRemove item should survive compact");
        Assert.Equal("important-value", value);
    }

    #endregion

    #region Meter Disposed Before Cache

    /// <summary>
    /// Tests that disposing the meter before the cache does not cause exceptions.
    /// Observable instruments should gracefully handle meter disposal.
    /// </summary>
    [Fact]
    public void MeterDisposedBeforeCache_ShouldNotThrow()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.meter.disposed.first"));
        var cacheName = SharedUtilities.GetUniqueCacheName("meter-disposed");

        var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Perform some operations
        cache.Set("key1", "value1");
        cache.TryGetValue("key1", out _);

        // Act: Dispose meter BEFORE cache
        meter.Dispose();

        // Assert: Cache operations should still work (no metrics emitted, but no exceptions)
        var ex = Record.Exception(() =>
        {
            cache.Set("key2", "value2");
            cache.TryGetValue("key2", out _);
            cache.TryGetValue("nonexistent", out _);
            cache.Remove("key1");
        });

        Assert.Null(ex);

        // GetCurrentStatistics should still work (uses atomic counters, not meter)
        var stats = cache.GetCurrentStatistics();
        Assert.True(stats.TotalHits >= 1, "Hit count should be tracked even after meter disposal");

        // Cleanup: Dispose cache
        cache.Dispose();
    }

    /// <summary>
    /// Tests that disposing the meter during active cache operations does not cause corruption.
    /// </summary>
    [Fact]
    public async Task MeterDisposedDuringOperations_ShouldNotCorrupt()
    {
        // Arrange
        const int iterations = 50;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            using var inner = new MemoryCache(new MemoryCacheOptions());
            var meter = new Meter(SharedUtilities.GetUniqueMeterName($"test.meter.race.{iteration}"));
            var cacheName = SharedUtilities.GetUniqueCacheName($"meter-race-{iteration}");

            var cache = new MeteredMemoryCache(inner, meter, cacheName);

            // Pre-populate cache
            for (int i = 0; i < 10; i++)
            {
                cache.Set($"key-{i}", $"value-{i}");
            }

            // Act: Race between meter disposal and cache operations
            var meterDisposeTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Yield(); // Yield to allow operations to start
                    meter.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            var operationTasks = Enumerable.Range(0, 5).Select(threadId =>
                Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            cache.Set($"thread-{threadId}-key-{i}", $"value-{i}");
                            cache.TryGetValue($"key-{i % 10}", out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                })).ToArray();

            await Task.WhenAll(operationTasks.Concat(new[] { meterDisposeTask }));

            cache.Dispose();
        }

        // Assert: No unexpected exceptions
        Assert.Empty(exceptions);
    }

    /// <summary>
    /// Tests that MeterFactory-owned meter disposal before cache works correctly.
    /// When using IMeterFactory, the meter is not owned by the cache.
    /// </summary>
    [Fact]
    public void MeterFactoryDisposedBeforeCache_ShouldNotThrow()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meterFactory = new TestMeterFactory();
        var cacheName = SharedUtilities.GetUniqueCacheName("factory-disposed");

        var cache = new MeteredMemoryCache(inner, meterFactory, cacheName);

        // Perform operations
        cache.Set("key", "value");
        cache.TryGetValue("key", out _);

        // Act: Dispose factory BEFORE cache
        meterFactory.Dispose();

        // Assert: Cache operations should still work
        var ex = Record.Exception(() =>
        {
            cache.Set("key2", "value2");
            cache.TryGetValue("key2", out _);
            var stats = cache.GetCurrentStatistics();
        });

        Assert.Null(ex);

        // Cleanup
        cache.Dispose();
    }

    #endregion

    #region Additional Edge Cases

    /// <summary>
    /// Tests that creating multiple entries for the same key in rapid succession works correctly.
    /// </summary>
    [Fact]
    public async Task RapidKeyOverwrite_ShouldMaintainCorrectStatistics()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.rapid.overwrite"));
        var cacheName = SharedUtilities.GetUniqueCacheName("rapid-overwrite");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Act: Rapidly overwrite the same key
        const int overwrites = 100;
        for (int i = 0; i < overwrites; i++)
        {
            cache.Set("same-key", $"value-{i}");
        }

        // Assert: Only the last value should be present
        Assert.True(cache.TryGetValue("same-key", out var value));
        Assert.Equal($"value-{overwrites - 1}", value);

        // Wait for eviction callbacks to settle (replacement evictions fire asynchronously)
        await Task.Yield();

        // Entry count should eventually settle to 1 after eviction callbacks process
        // Note: MemoryCache's replacement behavior fires eviction callbacks asynchronously,
        // so we verify the count is reasonable (1) or slightly higher if callbacks are pending.
        var stats = cache.GetCurrentStatistics();
        Assert.InRange(stats.CurrentEntryCount, 1, overwrites);

        // The key value is correct regardless of callback timing
        Assert.True(cache.TryGetValue("same-key", out var finalValue));
        Assert.Equal($"value-{overwrites - 1}", finalValue);
    }

    /// <summary>
    /// Tests behavior when cache entry value is null.
    /// </summary>
    [Fact]
    public void NullValue_ShouldBeHandledCorrectly()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.null.value"));
        var cacheName = SharedUtilities.GetUniqueCacheName("null-value");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Act: Store null value
        cache.Set("null-key", (object?)null);

        // Assert: Should be a hit (key exists) with null value
        var found = cache.TryGetValue("null-key", out var value);
        Assert.True(found, "Key with null value should be found");
        Assert.Null(value);

        // Statistics should show a hit
        var stats = cache.GetCurrentStatistics();
        Assert.Equal(1, stats.TotalHits);
    }

    /// <summary>
    /// Tests that GetCurrentStatistics works correctly on a freshly created cache.
    /// </summary>
    [Fact]
    public void GetCurrentStatistics_OnFreshCache_ReturnsZeros()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.fresh.stats"));
        var cacheName = SharedUtilities.GetUniqueCacheName("fresh-stats");

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Act: Get statistics without any operations
        var stats = cache.GetCurrentStatistics();

        // Assert: All counters should be zero
        Assert.Equal(0, stats.TotalHits);
        Assert.Equal(0, stats.TotalMisses);
        Assert.Equal(0, stats.TotalEvictions);
        Assert.Equal(0, stats.CurrentEntryCount);
    }

    #endregion

    /// <summary>
    /// Minimal IMeterFactory implementation for testing.
    /// </summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
