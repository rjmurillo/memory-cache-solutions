using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;

namespace Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, id: "ThroughputNet90")]
[JsonExporter]
public class CacheBenchmarks
{
    private MemoryCache _raw = null!; // recreated per iteration
    private CoalescingMemoryCache _coalescing = null!;
    private MeteredMemoryCache _metered = null!;
    private SingleFlightCache _singleFlight = null!;
    private SingleFlightLazyCache _singleFlightLazy = null!;

    private const string HitKey = "hit_key";
    private const string MissKey = "miss_key";
    private const int ChurnKeyCount = 4096; // power-of-two for bitmasking

    private readonly string[] _churnKeys;
    private int _churnIdx = -1; // first Interlocked.Increment -> 0
    private int _counter;

    // Cached delegates to avoid per-call lambda allocations
    private readonly Func<Task<int>> _incrementTaskFactory;
    private readonly Func<object, Task<int>> _incrementWithStateTaskFactory;
    private readonly Func<ICacheEntry, int> _incrementSyncFactory;

    public CacheBenchmarks()
    {
        // Stable data that does not depend on per-iteration cache instances.
        _churnKeys = Enumerable.Range(0, ChurnKeyCount).Select(i => $"k_{i}").ToArray();
        
        // Initialize cached delegates to avoid per-call lambda allocations while preserving the work
        _incrementTaskFactory = () => Task.FromResult(Interlocked.Increment(ref _counter));
        _incrementWithStateTaskFactory = _ => Task.FromResult(Interlocked.Increment(ref _counter));
        _incrementSyncFactory = _ => Interlocked.Increment(ref _counter);
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
        _churnIdx = -1; // first Interlocked.Increment -> 0
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
        _raw.TryGetValue(HitKey, out var obj);
        return obj is int v ? v : 0;
    }

    [Benchmark]
    public int RawMemoryCache_Miss()
    {
        _raw.Remove(MissKey);
        _raw.TryGetValue(MissKey, out var obj); // obj will be null/default
        return obj is int v ? v : 0;
    }

    // HIT PATHS (no artificial Task.Yield)
    [Benchmark]
    public async Task<int> SingleFlight_Hit() => await _singleFlight.GetOrCreateAsync(HitKey, TimeSpan.FromMinutes(5), _incrementTaskFactory);

    [Benchmark]
    public async Task<int> SingleFlightLazy_Hit() => await _singleFlightLazy.GetOrCreateAsync(HitKey, TimeSpan.FromMinutes(5), _incrementTaskFactory);

    [Benchmark]
    public async Task<int> Coalescing_Hit() => await _coalescing.GetOrCreateAsync(HitKey, _incrementWithStateTaskFactory);

    [Benchmark]
    public int Metered_Hit() => _metered.GetOrCreate(HitKey, _incrementSyncFactory);

    // MISS PATHS (factory returns quickly)
    [Benchmark]
    public async Task<int> SingleFlight_Miss()
    {
        var key = MissKey;
        _raw.Remove(key);
        return await _singleFlight.GetOrCreateAsync(key, TimeSpan.FromMinutes(5), _incrementTaskFactory);
    }

    [Benchmark]
    public async Task<int> SingleFlightLazy_Miss()
    {
        var key = MissKey + "_lazy";
        _raw.Remove(key);
        return await _singleFlightLazy.GetOrCreateAsync(key, TimeSpan.FromMinutes(5), _incrementTaskFactory);
    }

    [Benchmark]
    public async Task<int> Coalescing_Miss()
    {
        var key = MissKey + "_coal";
        _raw.Remove(key);
        return await _coalescing.GetOrCreateAsync(key, _incrementWithStateTaskFactory);
    }

    // CHURN / HIGH CARDINALITY (semaphore dictionary growth)
    [Benchmark]
    public async Task<int> SingleFlight_Churn()
    {
        var idx = Interlocked.Increment(ref _churnIdx) & (ChurnKeyCount - 1); // 0..ChurnKeyCount-1
        var key = _churnKeys[idx];
        _raw.Remove(key);
        return await _singleFlight.GetOrCreateAsync(key, TimeSpan.FromSeconds(30), _incrementTaskFactory);
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
