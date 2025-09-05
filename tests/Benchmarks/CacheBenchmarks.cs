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
    private int _counter;

    public CacheBenchmarks()
    {
        _coalescing = new CoalescingMemoryCache(_raw);
        _metered = new MeteredMemoryCache(_raw, new System.Diagnostics.Metrics.Meter("bench.meter"));
        _singleFlight = new SingleFlightCache(_raw);
        _singleFlightLazy = new SingleFlightLazyCache(_raw);
        // Warm prime values
        _raw.Set(Key, 42, TimeSpan.FromMinutes(1));
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
