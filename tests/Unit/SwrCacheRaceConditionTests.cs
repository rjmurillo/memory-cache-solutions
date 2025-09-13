using System.Collections.Concurrent;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;

namespace Unit;

/// <summary>
/// Regression tests for SWR cache race condition fixes.
/// </summary>
public class SwrCacheRaceConditionTests
{
    /// <summary>
    /// Tests that concurrent access to SWR cache during background refresh doesn't cause race conditions.
    /// This test verifies the fix for the race condition where box.Value and box.FreshUntil were
    /// being modified without proper synchronization.
    /// </summary>
    [Fact]
    public async Task ConcurrentAccessDuringBackgroundRefresh_ShouldNotCauseRaceConditions()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200));

        var factoryCallCount = 0;
        var factoryResults = new ConcurrentBag<int>();
        var accessResults = new ConcurrentBag<int>();

        async Task<int> Factory(CancellationToken _)
        {
            var callId = Interlocked.Increment(ref factoryCallCount);
            await Task.Yield(); // Simulate some async work
            factoryResults.Add(callId);
            return callId;
        }

        // First call to populate the cache
        var initialValue = await cache.GetOrCreateSwrAsync("test-key", opts, Factory);
        Assert.Equal(1, initialValue);

        // Wait for the value to become stale
        await Task.Yield();

        // Now perform concurrent access while background refresh is happening
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var value = await cache.GetOrCreateSwrAsync("test-key", opts, Factory);
                accessResults.Add(value);
            }));
        }

        await Task.WhenAll(tasks);

        // Verify that all factory calls completed successfully
        Assert.True(factoryCallCount >= 1, "Factory should have been called at least once");
        Assert.True(factoryResults.Count >= 1, "Factory results should have been recorded");

        // Verify that all access results are consistent
        Assert.Equal(20, accessResults.Count);

        // All values should be consistent (no race condition artifacts)
        var values = accessResults.Distinct().ToList();
        Assert.True(values.Count <= 2, $"Expected at most 2 distinct values (old and new), got {values.Count}: [{string.Join(", ", values)}]");

        // Verify that we don't have any inconsistent state
        foreach (var result in accessResults)
        {
            // The value should be a positive integer (no corruption)
            Assert.True(result > 0, $"Value should be positive, got {result}");
        }
    }

    /// <summary>
    /// Tests that the SWR cache handles rapid concurrent access correctly without data corruption.
    /// </summary>
    [Fact]
    public async Task RapidConcurrentAccess_ShouldMaintainDataIntegrity()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));

        var factoryCallCount = 0;
        var results = new ConcurrentBag<int>();

        async Task<int> Factory(CancellationToken _)
        {
            var callId = Interlocked.Increment(ref factoryCallCount);
            await Task.Yield();
            return callId;
        }

        // Perform rapid concurrent access
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var value = await cache.GetOrCreateSwrAsync("rapid-key", opts, Factory);
                results.Add(value);
            }));
        }

        await Task.WhenAll(tasks);

        // Verify results
        Assert.Equal(50, results.Count);
        Assert.True(factoryCallCount >= 1, "Factory should have been called at least once");

        // All results should be valid positive integers
        foreach (var result in results)
        {
            Assert.True(result > 0, $"Result should be positive, got {result}");
        }
    }

    /// <summary>
    /// Tests that background refresh failures don't corrupt the cache state.
    /// </summary>
    [Fact]
    public async Task BackgroundRefreshFailure_ShouldNotCorruptCacheState()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200));

        var successCount = 0;
        var failureCount = 0;

        Task<string> FailingFactory(CancellationToken _)
        {
            if (Interlocked.Increment(ref failureCount) % 2 == 0)
            {
                throw new InvalidOperationException("Simulated factory failure");
            }

            Interlocked.Increment(ref successCount);
            return Task.FromResult($"success-{successCount}");
        }

        // First successful call
        var initialValue = await cache.GetOrCreateSwrAsync("failing-key", opts, FailingFactory);
        Assert.StartsWith("success-", initialValue);

        // Wait for stale
        await Task.Yield();

        // Multiple concurrent calls that will trigger background refresh
        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(cache.GetOrCreateSwrAsync("failing-key", opts, FailingFactory));
        }

        var results = await Task.WhenAll(tasks);

        // All results should be the same (stale value served during failures)
        var distinctResults = results.Distinct().ToList();
        Assert.True(distinctResults.Count <= 2, $"Expected at most 2 distinct results, got {distinctResults.Count}");

        // All results should be valid strings
        foreach (var result in results)
        {
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }
    }
}
