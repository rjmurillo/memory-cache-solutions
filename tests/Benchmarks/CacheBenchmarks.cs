using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;

namespace Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
[SimpleJob(RuntimeMoniker.Net90, id: "ThroughputNet90")]
public class CacheBenchmarks
{
    private MemoryCache _raw = default!; // recreated per iteration
    private CoalescingMemoryCache _coalescing = default!;
    private MeteredMemoryCache _metered = default!;
    private SingleFlightCache _singleFlight = default!;
    private SingleFlightLazyCache _singleFlightLazy = default!;

    private const string HitKey = "hit_key";
    private const string MissKey = "miss_key";

    private readonly string[] _churnKeys;
    private int _churnIdx;
    private int _counter;

    public CacheBenchmarks()
    {
        // Stable data that does not depend on per-iteration cache instances.
        _churnKeys = Enumerable.Range(0, 4096).Select(i => "k_" + i.ToString()).ToArray();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _raw = new MemoryCache(new MemoryCacheOptions());
        _coalescing = new CoalescingMemoryCache(_raw);
        _metered = new MeteredMemoryCache(_raw, new System.Diagnostics.Metrics.Meter("bench.meter"));
        _singleFlight = new SingleFlightCache(_raw);
        _singleFlightLazy = new SingleFlightLazyCache(_raw);
        _raw.Set(HitKey, 42, TimeSpan.FromMinutes(5));
        _counter = 0;
        _churnIdx = 0;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // Dispose the underlying cache to release resources between iterations.
        _raw.Dispose();
    }

    // Baselines
    [Benchmark(Baseline = true)]
    public int RawMemoryCache_Hit()
    {
        _raw.TryGetValue(HitKey, out int v);
        return v;
    }

    [Benchmark]
    public int RawMemoryCache_Miss()
    {
        _raw.Remove(MissKey);
        _raw.TryGetValue(MissKey, out int v); // v will be default
        return v;
    }

    // HIT PATHS (no artificial Task.Yield)
    [Benchmark]
    public async Task<int> SingleFlight_Hit() => await _singleFlight.GetOrCreateAsync(HitKey, TimeSpan.FromMinutes(5), () => Task.FromResult(Interlocked.Increment(ref _counter)));

    [Benchmark]
    public async Task<int> SingleFlightLazy_Hit() => await _singleFlightLazy.GetOrCreateAsync(HitKey, TimeSpan.FromMinutes(5), () => Task.FromResult(Interlocked.Increment(ref _counter)));

    [Benchmark]
    public async Task<int> Coalescing_Hit() => await _coalescing.GetOrCreateAsync(HitKey, _ => Task.FromResult(Interlocked.Increment(ref _counter)));

    [Benchmark]
    public int Metered_Hit() => _metered.GetOrCreate(HitKey, _ => Interlocked.Increment(ref _counter));

    // MISS PATHS (factory returns quickly)
    [Benchmark]
    public async Task<int> SingleFlight_Miss()
    {
        var key = MissKey;
        _raw.Remove(key);
        return await _singleFlight.GetOrCreateAsync(key, TimeSpan.FromMinutes(5), () => Task.FromResult(1));
    }

    [Benchmark]
    public async Task<int> SingleFlightLazy_Miss()
    {
        var key = MissKey + "_lazy";
        _raw.Remove(key);
        return await _singleFlightLazy.GetOrCreateAsync(key, TimeSpan.FromMinutes(5), () => Task.FromResult(1));
    }

    [Benchmark]
    public async Task<int> Coalescing_Miss()
    {
        var key = MissKey + "_coal";
        _raw.Remove(key);
        return await _coalescing.GetOrCreateAsync(key, _ => Task.FromResult(1));
    }

    // CHURN / HIGH CARDINALITY (semaphore dictionary growth)
    [Benchmark]
    public async Task<int> SingleFlight_Churn()
    {
        var i = unchecked((uint)Interlocked.Increment(ref _churnIdx)) % (uint)_churnKeys.Length;
        var key = _churnKeys[i];
        _raw.Remove(key);
        return await _singleFlight.GetOrCreateAsync(key, TimeSpan.FromSeconds(30), () => Task.FromResult(1));
    }

    // Simulated heavier work (to compare overhead proportionally)
    private static Task<int> SimulatedWorkFactory()
    {
        // cheap deterministic pseudo work
        int sum = 0;
        for (int i = 0; i < 32; i++) sum += i;
        return Task.FromResult(sum);
    }

    [Benchmark]
    public async Task<int> SingleFlight_Hit_SimulatedWork() => await _singleFlight.GetOrCreateAsync(HitKey, TimeSpan.FromMinutes(5), SimulatedWorkFactory);
}
