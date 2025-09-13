using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.Tasks;

namespace Unit;

public class SingleFlightLazyCacheTests
{
    [Fact]
    public async Task ConcurrentCalls_InvokeFactoryOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);
        int factoryCalls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Yield();
            return 10;
        }

        var tasks = Enumerable.Range(0, 30)
            .Select(_ => sfl.GetOrCreateAsync("a", TimeSpan.FromMinutes(1), Factory))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(10, r));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task ReturnsCachedValue_SubsequentCalls()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);
        int factoryCalls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Yield();
            return 5;
        }

        var v1 = await sfl.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), Factory);
        var v2 = await sfl.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), Factory);

        Assert.Equal(5, v1);
        Assert.Equal(5, v2);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task RespectsTtl_ExpiresAndRecreates()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);
        int factoryCalls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Yield();
            return factoryCalls;
        }

        var a = await sfl.GetOrCreateAsync("ttl", TimeSpan.FromMilliseconds(80), Factory);
        await Task.Yield();
        var b = await sfl.GetOrCreateAsync("ttl", TimeSpan.FromMilliseconds(80), Factory);

        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task Cancellation_AppliesToAwait_NotUnderlyingTask()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> Factory()
        {
            started.SetResult();
            await Task.Yield();
            return 7;
        }

        var initial = sfl.GetOrCreateAsync("c", TimeSpan.FromMinutes(1), Factory);
        await started.Task;

        using var cts = new CancellationTokenSource();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sfl.GetOrCreateAsync("c", TimeSpan.FromMinutes(1), Factory, ct: cts.Token));
        Assert.Equal(7, await initial);
    }
}
