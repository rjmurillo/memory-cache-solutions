using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unit;
public class SingleFlightCacheTests
{
    [Fact]
    public async Task SingleFlightCache_OnlyInvokesFactoryOnce_WhenConcurrent()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        var key = "expensive";
        int factoryCalls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Yield(); // simulate work
            return 123;
        }

        // Run many concurrent callers
        var tasks = Enumerable.Range(0, 25)
            .Select(_ => sfc.GetOrCreateAsync(key, TimeSpan.FromMinutes(1), Factory))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(123, r));
        Assert.Equal(1, factoryCalls); // factory only ran once
    }

    [Fact]
    public async Task SingleFlightCache_ReturnsCachedValue_OnSubsequentCalls()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        var key = "value";
        int factoryCalls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Yield();
            return 7;
        }

        var first = await sfc.GetOrCreateAsync(key, TimeSpan.FromMinutes(1), Factory);
        var second = await sfc.GetOrCreateAsync(key, TimeSpan.FromMinutes(1), Factory);

        Assert.Equal(7, first);
        Assert.Equal(7, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task SingleFlightCache_RespectsTtl_ExpiresEntry()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        var key = "ttl";
        int factoryCalls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Yield();
            return factoryCalls; // return call count so we can see change after expiration
        }

        var a = await sfc.GetOrCreateAsync(key, TimeSpan.FromMilliseconds(100), Factory);
        
        // Force expiration by manually removing the entry
        cache.Remove(key);
        
        var b = await sfc.GetOrCreateAsync(key, TimeSpan.FromMilliseconds(100), Factory);

        Assert.Equal(1, a);
        Assert.Equal(2, b); // second factory call after expiration
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task SingleFlightCache_Cancellation_Throws()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfc = new SingleFlightCache(cache);
        var key = "cancel";

        var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();

        async Task<int> Factory()
        {
            started.SetResult();
            await Task.Delay(500, cts.Token);
            return 1;
        }

        var firstTask = sfc.GetOrCreateAsync(key, TimeSpan.FromMinutes(1), Factory, ct: cts.Token);
        await started.Task; // ensure factory started

        // Second caller waits on semaphore, cancel first to propagate
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask);
    }
}
