using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;
using System.Diagnostics.Metrics;

namespace Benchmarks;

/// <summary>
/// Benchmarks focused on contention scenarios (multiple concurrent callers for same key) vs hit/miss separation.
/// This class isolates contention so other benchmark classes can remain single-threaded for clearer micro-metrics.
/// Tests MeteredMemoryCache and OptimizedMeteredMemoryCache under concurrent load.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[JsonExporter]
public class ContentionBenchmarks
{
    private MemoryCache _raw = null!; // initialized in GlobalSetup
    private MeteredMemoryCache _metered = null!; // initialized in GlobalSetup
    private OptimizedMeteredMemoryCache _optimized = null!; // initialized in GlobalSetup
    private Meter _meter = null!;

    private const string HotKey = "hot_key";
    private int _valueCounter;

    /// <summary>
    /// Degree of parallelism used inside each benchmark invocation to hammer the same key.
    /// </summary>
    [Params(1, 4, 16, 64)]
    public int Concurrency { get; set; }

    /// <summary>
    /// Sets up the benchmark environment by initializing cache instances.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _valueCounter = 0;
        _raw = new MemoryCache(new MemoryCacheOptions());
        _meter = new Meter("ContentionBenchmarkMeter");

        var meteredInner = new MemoryCache(new MemoryCacheOptions());
        _metered = new MeteredMemoryCache(meteredInner, _meter, "contention-cache", disposeInner: true);

        var optimizedInner = new MemoryCache(new MemoryCacheOptions());
        _optimized = new OptimizedMeteredMemoryCache(optimizedInner, _meter, "contention-cache", disposeInner: true);

        // Pre-populate to measure pure hit contention separately from miss contention.
        _raw.Set(HotKey, 42, TimeSpan.FromMinutes(5));
        _metered.Set(HotKey, 42, TimeSpan.FromMinutes(5));
        _optimized.Set(HotKey, 42, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Per-iteration reset so each iteration starts from a known state: ensure hit key present and reset counters.
    /// Miss benchmarks explicitly remove the key; hit benchmarks rely on presence.
    /// </summary>
    [IterationSetup]
    public void IterationSetup()
    {
        // Reset the counter so relative increments remain comparable between iterations.
        _valueCounter = 0;

        if (!_raw.TryGetValue(HotKey, out _))
        {
            _raw.Set(HotKey, 42, TimeSpan.FromMinutes(5));
        }
        if (!_metered.TryGetValue(HotKey, out _))
        {
            _metered.Set(HotKey, 42, TimeSpan.FromMinutes(5));
        }
        if (!_optimized.TryGetValue(HotKey, out _))
        {
            _optimized.Set(HotKey, 42, TimeSpan.FromMinutes(5));
        }
    }

    private Task<int> SimulatedFactoryAsync()
    {
        // Minimal work; increment to ensure value materializes uniquely on a miss.
        var val = Interlocked.Increment(ref _valueCounter);
        return Task.FromResult(val);
    }

    /// <summary>
    /// Raw MemoryCache contended hit baseline.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<object?> Raw_Contention_Hit()
    {
        var tasks = new Task<object?>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.FromResult(_raw.Get(HotKey));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// MeteredMemoryCache contended hit.
    /// </summary>
    [Benchmark]
    public async Task<object?> Metered_Contention_Hit()
    {
        var tasks = new Task<object?>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.FromResult(_metered.Get(HotKey));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// OptimizedMeteredMemoryCache contended hit.
    /// </summary>
    [Benchmark]
    public async Task<object?> Optimized_Contention_Hit()
    {
        var tasks = new Task<object?>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.FromResult(_optimized.Get(HotKey));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// Raw MemoryCache contended miss baseline.
    /// </summary>
    [Benchmark]
    public async Task<object?> Raw_Contention_Miss()
    {
        _raw.Remove(HotKey);
        var tasks = new Task<object?>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.FromResult(_raw.Get(HotKey));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// MeteredMemoryCache contended miss.
    /// </summary>
    [Benchmark]
    public async Task<object?> Metered_Contention_Miss()
    {
        _metered.Remove(HotKey);
        var tasks = new Task<object?>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.FromResult(_metered.Get(HotKey));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// OptimizedMeteredMemoryCache contended miss.
    /// </summary>
    [Benchmark]
    public async Task<object?> Optimized_Contention_Miss()
    {
        _optimized.Remove(HotKey);
        var tasks = new Task<object?>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.FromResult(_optimized.Get(HotKey));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// Cleanup after all benchmarks: dispose underlying caches to avoid skewing MemoryDiagnoser / GC metrics.
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _raw.Dispose();
        _metered.Dispose();
        _optimized.Dispose();
        _meter.Dispose();
    }
}
