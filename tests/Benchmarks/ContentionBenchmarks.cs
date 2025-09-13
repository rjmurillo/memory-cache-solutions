using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Caching.Memory;
using CacheImplementations;

namespace Benchmarks;

/// <summary>
/// Benchmarks focused on contention scenarios (multiple concurrent callers for same key) vs hit/miss separation.
/// This class isolates contention so other benchmark classes can remain single-threaded for clearer micro-metrics.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[JsonExporter]
public class ContentionBenchmarks
{
    private MemoryCache _raw = null!; // initialized in GlobalSetup
    private CoalescingMemoryCache _coalescing = null!; // initialized in GlobalSetup

    private const string HotKey = "hot_key";
    private int _valueCounter;

    /// <summary>
    /// Degree of parallelism used inside each benchmark invocation to hammer the same key.
    /// </summary>
    [Params(1, 4, 16, 64)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _valueCounter = 0;
        _raw = new MemoryCache(new MemoryCacheOptions());
        _coalescing = new CoalescingMemoryCache(_raw);

        // Pre-populate to measure pure hit contention separately from miss contention.
        _raw.Set(HotKey, 42, TimeSpan.FromMinutes(5));
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
    }

    private Task<int> SimulatedFactoryAsync()
    {
        // Minimal work; increment to ensure value materializes uniquely on a miss.
        var val = Interlocked.Increment(ref _valueCounter);
        return Task.FromResult(val);
    }


    /// <summary>
    /// Coalescing cache contended hit.
    /// </summary>
    [Benchmark]
    public async Task<int> Coalescing_Contention_Hit()
    {
        var tasks = new Task<int>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _coalescing.GetOrCreateAsync(HotKey, _ => Task.FromResult(100));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// Coalescing cache contended miss.
    /// </summary>
    [Benchmark]
    public async Task<int> Coalescing_Contention_Miss()
    {
        _raw.Remove(HotKey);
        var tasks = new Task<int>[Concurrency];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _coalescing.GetOrCreateAsync(HotKey, _ => Task.FromResult(Interlocked.Increment(ref _valueCounter)));
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// Cleanup after all benchmarks: dispose underlying MemoryCache to avoid skewing MemoryDiagnoser / GC metrics.
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _raw.Dispose();
    }
}
