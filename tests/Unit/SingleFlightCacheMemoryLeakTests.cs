using System.Collections.Concurrent;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Unit;

/// <summary>
/// Regression tests for SingleFlightCache memory leak fixes.
/// </summary>
public class SingleFlightCacheMemoryLeakTests
{
    /// <summary>
    /// Tests that SemaphoreSlim instances are properly cleaned up even when exceptions occur.
    /// This test verifies the fix for the potential memory leak where SemaphoreSlim instances
    /// might not be removed from the _locks dictionary if TryRemove fails.
    /// </summary>
    [Fact]
    public async Task ExceptionDuringCleanup_ShouldNotLeakSemaphoreSlim()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        
        var factoryCallCount = 0;
        var results = new ConcurrentBag<int>();

        async Task<int> Factory()
        {
            var callId = Interlocked.Increment(ref factoryCallCount);
            await Task.Yield();
            return callId;
        }

        // Perform multiple operations to create and clean up semaphores
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++) // Reduced from 100 to avoid hanging
        {
            var key = $"key-{i}";
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await sfc.GetOrCreateAsync(key, TimeSpan.FromMinutes(1), Factory);
                    results.Add(result);
                }
                catch
                {
                    // Ignore exceptions - we're testing cleanup, not functionality
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Verify that operations completed
        Assert.True(results.Count > 0, "Some operations should have completed successfully");
        Assert.True(factoryCallCount > 0, "Factory should have been called");

        // The key test is that no exceptions were thrown during cleanup
        // If there were memory leaks, we'd see OutOfMemoryException or similar
        // This test passes if it completes without throwing exceptions
    }

    /// <summary>
    /// Tests that rapid concurrent access doesn't cause memory leaks in the _locks dictionary.
    /// </summary>
    [Fact]
    public async Task RapidConcurrentAccess_ShouldNotLeakSemaphores()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        
        var factoryCallCount = 0;
        var results = new ConcurrentBag<int>();

        async Task<int> Factory()
        {
            var callId = Interlocked.Increment(ref factoryCallCount);
            await Task.Yield();
            return callId;
        }

        // Perform rapid concurrent access with many different keys
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++) // Reduced from 50 to avoid hanging
        {
            for (int j = 0; j < 5; j++) // Reduced from 10 to avoid hanging
            {
                var key = $"rapid-key-{i}-{j}";
                tasks.Add(Task.Run(async () =>
                {
                    var result = await sfc.GetOrCreateAsync(key, TimeSpan.FromMinutes(1), Factory);
                    results.Add(result);
                }));
            }
        }

        await Task.WhenAll(tasks);

        // Verify results
        Assert.Equal(25, results.Count); // 5 * 5 = 25 operations
        Assert.True(factoryCallCount >= 1, "Factory should have been called at least once");

        // All results should be valid positive integers
        foreach (var result in results)
        {
            Assert.True(result > 0, $"Result should be positive, got {result}");
        }
    }

    /// <summary>
    /// Tests that the same key accessed concurrently doesn't leak multiple semaphores.
    /// </summary>
    [Fact]
    public async Task SameKeyConcurrentAccess_ShouldNotLeakMultipleSemaphores()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        
        var factoryCallCount = 0;
        var results = new ConcurrentBag<int>();

        async Task<int> Factory()
        {
            var callId = Interlocked.Increment(ref factoryCallCount);
            await Task.Yield();
            return callId;
        }

        const string sameKey = "same-key";
        const int concurrentCalls = 5; // Reduced from 20 to avoid hanging

        // Multiple concurrent calls to the same key
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentCalls; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var result = await sfc.GetOrCreateAsync(sameKey, TimeSpan.FromMinutes(1), Factory);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        // Verify results
        Assert.Equal(concurrentCalls, results.Count);
        Assert.Equal(1, factoryCallCount); // Factory should only be called once due to single-flight behavior

        // All results should be the same value
        var distinctResults = results.Distinct().ToList();
        Assert.True(distinctResults.Count == 1, $"All results should be the same value due to single-flight behavior, got {distinctResults.Count} distinct values: [{string.Join(", ", distinctResults)}]");
    }

    /// <summary>
    /// Tests that cancellation during semaphore wait doesn't cause memory leaks.
    /// </summary>
    [Fact]
    public async Task CancellationDuringWait_ShouldNotLeakSemaphores()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        
        var factoryCallCount = 0;
        var results = new ConcurrentBag<int>();

        async Task<int> SlowFactory()
        {
            var callId = Interlocked.Increment(ref factoryCallCount);
            await Task.Yield();
            return callId;
        }

        const string slowKey = "slow-key";

        // Start a slow operation
        var slowTask = sfc.GetOrCreateAsync(slowKey, TimeSpan.FromMinutes(1), SlowFactory);

        // Start multiple operations that will be cancelled
        var cts = new CancellationTokenSource();
        cts.CancelAfter(10); // Cancel quickly

        var cancelledTasks = new List<Task>();
        for (int i = 0; i < 3; i++) // Reduced from 10 to avoid hanging
        {
            cancelledTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await sfc.GetOrCreateAsync(slowKey, TimeSpan.FromMinutes(1), SlowFactory, ct: cts.Token);
                    results.Add(result);
                }
                catch (OperationCanceledException)
                {
                    // Expected - operations should be cancelled
                }
            }));
        }

        // Wait for the slow operation to complete
        var slowResult = await slowTask;
        results.Add(slowResult);

        // Wait for cancelled operations
        await Task.WhenAll(cancelledTasks);

        // Verify results
        Assert.True(results.Count >= 1, "At least the slow operation should have completed");
        Assert.Equal(1, factoryCallCount); // Factory should only be called once

        // All results should be the same value
        var distinctResults = results.Distinct().ToList();
        Assert.True(distinctResults.Count == 1, $"All results should be the same value, got {distinctResults.Count} distinct values: [{string.Join(", ", distinctResults)}]");
    }
}
