using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

namespace Unit;

public class CoalescingMemoryCacheTests
{
    [Fact]
    public async Task ConcurrentMisses_Coalesce_ToSingleFactoryInvocation()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cache = new CoalescingMemoryCache(inner);

        int calls = 0;
        async Task<int> Factory(ICacheEntry entry)
        {
            Interlocked.Increment(ref calls);
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            await Task.Yield();
            return 42;
        }

        var tasks = Enumerable.Range(0, 30).Select(_ => cache.GetOrCreateAsync("k", Factory)).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(42, r));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SubsequentCalls_AfterPopulation_AreHits()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cache = new CoalescingMemoryCache(inner);
        int calls = 0;
        async Task<int> Factory(ICacheEntry entry)
        {
            calls++;
            await Task.Yield();
            return 5;
        }

        var a = await cache.GetOrCreateAsync("k", Factory);
        var b = await cache.GetOrCreateAsync("k", Factory);

        Assert.Equal(5, a);
        Assert.Equal(5, b);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedFactory_AllowsRetry()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cache = new CoalescingMemoryCache(inner);
        int attempts = 0;

        async Task<int> Factory(ICacheEntry entry)
        {
            attempts++;
            await Task.Yield();
            if (attempts < 2) throw new InvalidOperationException("boom");
            return 7;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync("k", Factory));
        var val = await cache.GetOrCreateAsync("k", Factory);

        Assert.Equal(7, val);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ExistingSet_ShortCircuitsFactory()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cache = new CoalescingMemoryCache(inner);
        cache.Set("k", 11);

        bool called = false;
        Task<int> Factory(ICacheEntry entry)
        {
            called = true;
            return Task.FromResult(99);
        }

        var v = await cache.GetOrCreateAsync("k", Factory);
        Assert.False(called);
        Assert.Equal(11, v);
    }
}
