using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

namespace Unit;

public class SwrCacheTests
{
    [Fact]
    public async Task FreshValue_Returned_NoBackgroundRefresh()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500));
        int calls = 0;

        Task<int> Factory(CancellationToken _) { calls++; return Task.FromResult(1); }

        var v1 = await cache.GetOrCreateSwrAsync("x", opts, Factory);
        var v2 = await cache.GetOrCreateSwrAsync("x", opts, Factory);

        Assert.Equal(1, v1);
        Assert.Equal(1, v2);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StaleValue_TriggersBackgroundRefresh_ServesOldThenNew()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(300));
        int calls = 0;
        int produced = 0;

        async Task<int> Factory(CancellationToken _)
        {
            await Task.Delay(10, _);
            calls++;
            return ++produced;
        }

        var first = await cache.GetOrCreateSwrAsync("k", opts, Factory);
        await Task.Delay(120); // become stale
        var stale = await cache.GetOrCreateSwrAsync("k", opts, Factory); // should trigger refresh
        Assert.Equal(first, stale); // stale served

        // Wait for background refresh to finish
        await Task.Delay(120);
        var fresh = await cache.GetOrCreateSwrAsync("k", opts, Factory);

        Assert.True(fresh > stale);
        Assert.True(calls >= 2);
    }

    [Fact]
    public async Task Expired_Completely_Recomputes()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(80));
        int calls = 0;

        Task<int> Factory(CancellationToken _) { calls++; return Task.FromResult(calls); }

        var a = await cache.GetOrCreateSwrAsync("k2", opts, Factory);
        await Task.Delay(200); // exceed ttl + stale causing eviction
        var b = await cache.GetOrCreateSwrAsync("k2", opts, Factory);

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public async Task BackgroundFailure_DoesNotThrow_ToCaller()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new SwrOptions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200));
        int succeedCalls = 0;
        bool fail = false;

        Task<int> Factory(CancellationToken _)
        {
            if (fail)
            {
                throw new InvalidOperationException("boom");
            }
            succeedCalls++;
            return Task.FromResult(10);
        }

        _ = await cache.GetOrCreateSwrAsync("z", opts, Factory);
        await Task.Delay(70); // stale
        fail = true; // next refresh will fail
        var stale = await cache.GetOrCreateSwrAsync("z", opts, Factory); // triggers background failure
        await Task.Delay(100);
        fail = false;
        var fresh = await cache.GetOrCreateSwrAsync("z", opts, Factory); // should refresh successfully if stale again

        Assert.Equal(10, stale);
        Assert.Equal(10, fresh);
        Assert.True(succeedCalls >= 1);
    }
}
