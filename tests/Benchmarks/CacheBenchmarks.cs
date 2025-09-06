using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;

namespace Benchmarks;

[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
public class CacheBenchmarks
{
    private readonly MemoryCache _raw = new(new MemoryCacheOptions());
    private readonly CoalescingMemoryCache _coalescing;
    private readonly MeteredMemoryCache _metered;
    private readonly SingleFlightCache _singleFlight;
    private readonly SingleFlightLazyCache _singleFlightLazy;

    private const string Key = "key";
    private const string KeyVT = "keyVT"; // used only for separate key space now
    private int _counter;

    private readonly string[] _missKeys;
    private int _missIdx;

    public CacheBenchmarks()
    {
        _coalescing = new CoalescingMemoryCache(_raw);
        _metered = new MeteredMemoryCache(_raw, new System.Diagnostics.Metrics.Meter("bench.meter"));
        _singleFlight = new SingleFlightCache(_raw);
        _singleFlightLazy = new SingleFlightLazyCache(_raw);
        // Warm prime values for hit path benchmarks
        _raw.Set(Key, 42, TimeSpan.FromMinutes(1));
        _ = _singleFlight.GetOrCreateAsync(KeyVT, TimeSpan.FromMinutes(1), () => Task.FromResult(123)).GetAwaiter().GetResult();

        _missKeys = Enumerable.Range(0, 1024).Select(i => "miss_" + i.ToString()).ToArray();
    }

    // Async baseline to compare with async wrappers
    [Benchmark(Baseline = true)]
    public Task<int> RawMemoryCache_HitAsync()
    {
        _raw.TryGetValue(Key, out int v);
        return Task.FromResult(v);
    }

    [Benchmark]
    public async Task<int> SingleFlightCache_HitAsync() => await _singleFlight.GetOrCreateAsync(Key, TimeSpan.FromMinutes(1), () => Task.FromResult(Interlocked.Increment(ref _counter)));

    [Benchmark]
    public async Task<int> SingleFlightCache_SecondKey_HitAsync() => await _singleFlight.GetOrCreateAsync(KeyVT, TimeSpan.FromMinutes(1), () => Task.FromResult(123));

    [Benchmark]
    public async Task<int> SingleFlightCache_MissAsync()
    {
        var key = _missKeys[unchecked((uint)Interlocked.Increment(ref _missIdx)) % _missKeys.Length];
        // Force eviction to simulate periodic misses by clearing the key first
        _raw.Remove(key);
        return await _singleFlight.GetOrCreateAsync(key, TimeSpan.FromSeconds(30), () => Task.FromResult(1));
    }

    [Benchmark]
    public async Task<int> SingleFlightLazyCache_HitAsync() => await _singleFlightLazy.GetOrCreateAsync(Key, TimeSpan.FromMinutes(1), () => Task.FromResult(Interlocked.Increment(ref _counter)));

    [Benchmark]
    public async Task<int> Coalescing_GetOrCreateAsync()
    {
        return await _coalescing.GetOrCreateAsync(Key, async e =>
        {
            await Task.Yield();
            return Interlocked.Increment(ref _counter);
        });
    }

    [Benchmark]
    public int Metered_GetOrCreate()
    {
        return _metered.GetOrCreate(Key, _ => Interlocked.Increment(ref _counter));
    }

    private readonly SwrOptions _swrOpts = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    [Benchmark]
    public async Task<int> Swr_GetOrCreateAsync()
    {
        return await _raw.GetOrCreateSwrAsync(Key, _swrOpts, async _ =>
        {
            await Task.Yield();
            return Interlocked.Increment(ref _counter);
        });
    }
}
