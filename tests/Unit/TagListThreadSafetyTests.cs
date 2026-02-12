using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Unit;

/// <summary>
/// Comprehensive tests to reproduce and validate TagList enumeration thread-safety issues in MeteredMemoryCache.
/// These tests target the specific scenario where concurrent metric emission and eviction callbacks
/// access the shared TagList field, potentially causing InvalidOperationException during enumeration.
/// </summary>
public class TagListThreadSafetyTests
{
    private sealed class TestMetricsListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<Exception> _exceptions = new();
        private readonly object _lock = new();

        public IReadOnlyList<Exception> Exceptions
        {
            get
            {
                lock (_lock)
                {
                    return _exceptions.ToList();
                }
            }
        }

        public TestMetricsListener(params string[] instrumentNames)
        {
            _listener.InstrumentPublished = (inst, listener) =>
            {
                if (instrumentNames.Contains(inst.Name))
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };

            _listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                try
                {
                    // Force enumeration of tags to trigger potential thread-safety issues
                    var tagList = tags.ToArray();
                    var tagCount = tagList.Length;

                    // Additional enumeration that might expose race conditions
                    foreach (var tag in tags)
                    {
                        var key = tag.Key;
                        var value = tag.Value;
                    }
                }
                catch (Exception ex)
                {
                    lock (_lock)
                    {
                        _exceptions.Add(ex);
                    }
                }
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public void ConcurrentMetricEmission_WithSharedTagList_ShouldNotThrow()
    {
        // This test reproduces the scenario where multiple threads simultaneously emit metrics
        // using the shared _tags TagList field, which can cause enumeration exceptions
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.concurrent.metrics"));
        using var listener = new TestMetricsListener(meter.Name, "cache.lookups");

        var cache = new MeteredMemoryCache(inner, meter, "concurrent-cache");

        // Pre-populate some cache entries to ensure hits and misses
        for (int i = 0; i < 10; i++)
        {
            cache.Set($"existing-key-{i}", $"value-{i}");
        }

        var exceptions = new ConcurrentBag<Exception>();
        const int ThreadCount = 50;
        const int OperationsPerThread = 100;

        // Simulate high-concurrency cache access that would trigger simultaneous metric emission
        Parallel.For(0, ThreadCount, threadId =>
        {
            try
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var key = $"key-{threadId}-{i}";

                    // Mix of operations that will cause hits, misses, and metric emissions
                    if (i % 3 == 0)
                    {
                        // This will cause a miss and metric emission
                        cache.TryGetValue($"nonexistent-{key}", out _);
                    }
                    else if (i % 3 == 1)
                    {
                        // This will cause a hit and metric emission (for existing keys)
                        cache.TryGetValue($"existing-key-{i % 10}", out _);
                    }
                    else
                    {
                        // This will create new entries and potential future hits
                        cache.Set(key, $"value-{threadId}-{i}");
                        cache.TryGetValue(key, out _); // Immediate hit
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Verify no exceptions occurred during concurrent metric emission
        Assert.Empty(exceptions);
        Assert.Empty(listener.Exceptions);
    }

    [Fact]
    public async Task ConcurrentEvictionCallbacks_WithSharedTagList_ShouldNotThrow()
    {
        // This test reproduces the scenario where eviction callbacks execute concurrently
        // and attempt to enumerate the shared _tags TagList, causing thread-safety issues
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.concurrent.evictions"));
        using var listener = new TestMetricsListener(meter.Name, "cache.evictions");

        var cache = new MeteredMemoryCache(inner, meter, "eviction-cache");
        var exceptions = new ConcurrentBag<Exception>();
        const int ConcurrentEvictions = 100;

        // Create multiple cancellation tokens to trigger simultaneous evictions
        var cancellationTokenSources = new List<CancellationTokenSource>();
        for (int i = 0; i < ConcurrentEvictions; i++)
        {
            cancellationTokenSources.Add(new CancellationTokenSource());
        }

        try
        {
            // Set up cache entries with different expiration triggers
            Parallel.For(0, ConcurrentEvictions, i =>
            {
                try
                {
                    var options = new MemoryCacheEntryOptions();
                    options.AddExpirationToken(new CancellationChangeToken(cancellationTokenSources[i].Token));
                    cache.Set($"eviction-key-{i}", $"value-{i}", options);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Trigger all evictions simultaneously to cause concurrent callback execution
            Parallel.ForEach(cancellationTokenSources, cts =>
            {
                try
                {
                    cts.Cancel();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Force eviction processing by accessing entries and compacting
            Parallel.For(0, ConcurrentEvictions, i =>
            {
                try
                {
                    cache.TryGetValue($"eviction-key-{i}", out _);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Trigger compaction to ensure eviction callbacks are processed
            inner.Compact(0.0);

            // Give some time for asynchronous eviction callbacks to complete
            await Task.Yield();
        }
        finally
        {
            foreach (var cts in cancellationTokenSources)
            {
                cts.Dispose();
            }
        }

        // Verify no exceptions occurred during concurrent eviction processing
        Assert.Empty(exceptions);
        Assert.Empty(listener.Exceptions);
    }

    [Fact]
    public async Task MixedConcurrentOperations_WithTagListEnumeration_ShouldNotThrow()
    {
        // This test combines cache operations and evictions to maximize the probability
        // of concurrent TagList enumeration, which is the root cause of thread-safety issues
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.mixed.operations"));
        using var listener = new TestMetricsListener(meter.Name, "cache.lookups", "cache.evictions");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "mixed-operations-cache",
            AdditionalTags = { ["environment"] = "test", ["region"] = "us-west" }
        };
        var cache = new MeteredMemoryCache(inner, meter, options);
        var exceptions = new ConcurrentBag<Exception>();

        const int ThreadCount = 20;
        const int OperationsPerThread = 50;

        // Run mixed operations that will cause concurrent TagList access
        Parallel.For(0, ThreadCount, threadId =>
        {
            try
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var key = $"mixed-key-{threadId}-{i}";

                    switch (i % 5)
                    {
                        case 0:
                            // Miss scenario - accesses _tags for metric emission
                            cache.TryGetValue($"nonexistent-{key}", out _);
                            break;

                        case 1:
                            // Set with expiration - will register eviction callback that accesses _tags
                            var cts = new CancellationTokenSource();
                            var entryOptions = new MemoryCacheEntryOptions();
                            entryOptions.AddExpirationToken(new CancellationChangeToken(cts.Token));
                            cache.Set(key, $"value-{i}", entryOptions);

                            // Immediately cancel to trigger eviction callback
                            cts.Cancel();
                            cache.TryGetValue(key, out _); // Trigger eviction processing
                            cts.Dispose();
                            break;

                        case 2:
                            // GetOrCreate miss scenario - accesses _tags twice (miss + eviction registration)
                            cache.GetOrCreate($"create-{key}", entry => $"created-{i}");
                            break;

                        case 3:
                            // Set and immediate hit - accesses _tags for metric emission
                            cache.Set(key, $"hit-value-{i}");
                            cache.TryGetValue(key, out _);
                            break;

                        case 4:
                            // Remove operation - may trigger eviction callback
                            cache.Set($"remove-{key}", $"remove-value-{i}");
                            cache.Remove($"remove-{key}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Force final compaction to trigger any remaining evictions
        inner.Compact(0.0);
        await Task.Yield(); // Allow async eviction callbacks to complete

        // Verify no thread-safety exceptions occurred
        Assert.Empty(exceptions);
        Assert.Empty(listener.Exceptions);
    }

    [Fact]
    public async Task StressTest_TagListEnumeration_UnderExtremeLoad()
    {
        // Extreme stress test designed to maximize the probability of exposing
        // race conditions in TagList enumeration under very high load
        using var inner = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.stress.enumeration"));
        using var listener = new TestMetricsListener(meter.Name, "cache.lookups", "cache.evictions");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "stress-test-cache",
            AdditionalTags =
            {
                ["service"] = "test-service",
                ["version"] = "1.0.0",
                ["datacenter"] = "us-west-2a",
                ["team"] = "platform"
            }
        };
        var cache = new MeteredMemoryCache(inner, meter, options);
        var exceptions = new ConcurrentBag<Exception>();

        var ThreadCount = Environment.ProcessorCount * 4; // High thread contention
        const int OperationsPerThread = 200;

        var barrier = new Barrier(ThreadCount);

        // Synchronized start to maximize contention
        Parallel.For(0, ThreadCount, threadId =>
        {
            try
            {
                barrier.SignalAndWait(); // All threads start simultaneously

                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var key = $"stress-{threadId}-{i}";

                    // Rapid-fire operations to stress TagList enumeration
                    cache.TryGetValue($"miss-{key}", out _);                    // Miss + metric

                    // Set with size for SizeLimit compliance
                    var setOptions = new MemoryCacheEntryOptions { Size = 1 };
                    cache.Set(key, $"value-{i}", setOptions);                   // Set + eviction callback
                    cache.TryGetValue(key, out _);                             // Hit + metric

                    // GetOrCreate with size specification
                    cache.GetOrCreate($"create-{key}", entry =>
                    {
                        entry.Size = 1;
                        return $"created-{i}";
                    });   // Miss + metric + callback

                    // Force evictions by exceeding cache size limit
                    if (i % 10 == 0)
                    {
                        for (int j = 0; j < 50; j++)
                        {
                            var overflowOptions = new MemoryCacheEntryOptions { Size = 1 };
                            cache.Set($"overflow-{threadId}-{i}-{j}", $"overflow-{j}", overflowOptions);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Force final processing of any remaining evictions
        inner.Compact(0.0);
        await Task.Yield();

        // Verify no thread-safety violations occurred under extreme load
        Assert.Empty(exceptions);
        Assert.Empty(listener.Exceptions);
    }

    [Fact]
    public void ReproduceInvalidOperationException_DuringTagListEnumeration()
    {
        // This test specifically attempts to reproduce InvalidOperationException
        // that occurs when TagList is modified during enumeration
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.invalid.operation"));

        // Create a custom meter listener that intentionally enumerates tags multiple times
        // to increase the chance of hitting the enumeration during modification window
        var exceptions = new ConcurrentBag<Exception>();
        using var listener = new MeterListener();

        listener.InstrumentPublished = (inst, meterListener) =>
        {
            if (inst.Name.StartsWith("cache."))
            {
                meterListener.EnableMeasurementEvents(inst);
            }
        };

        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            try
            {
                // Multiple enumerations to increase race condition probability
                for (int i = 0; i < 3; i++)
                {
                    var tagArray = tags.ToArray();
                    foreach (var tag in tags)
                    {
                        var key = tag.Key;
                        var value = tag.Value?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });
        listener.Start();

        var cache = new MeteredMemoryCache(inner, meter, "reproduction-cache");
        const int ConcurrentOperations = 1000;

        // Execute operations that will trigger concurrent metric emissions
        Parallel.For(0, ConcurrentOperations, i =>
        {
            try
            {
                var key = $"reproduce-{i}";

                // Operations designed to cause simultaneous TagList access
                if (i % 2 == 0)
                {
                    cache.TryGetValue($"nonexistent-{key}", out _); // Miss
                    cache.Set(key, $"value-{i}");                   // Set
                    cache.TryGetValue(key, out _);                  // Hit
                }
                else
                {
                    cache.GetOrCreate(key, entry => $"created-{i}"); // Miss + Set
                    cache.Remove(key);                               // Eviction
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Check for specific InvalidOperationException that indicates TagList enumeration issues
        var invalidOperationExceptions = exceptions.OfType<InvalidOperationException>().ToList();

        // For now, we expect no exceptions (this will change once we implement the fix)
        // This test documents the expected behavior and will help validate the fix
        Assert.Empty(exceptions);

        listener.Dispose();
    }
}
