using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

namespace Benchmarks;

/// <summary>
/// Benchmarks comparing the overhead of different metric tracking approaches:
/// - System.Diagnostics.Metrics.Counter&lt;T&gt;.Add()
/// - Interlocked.Increment/Add
/// - No metrics (baseline)
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[JsonExporter]
public class MetricsOverheadBenchmarks
{
    private Counter<long> _counter = null!;
    private Meter _meter = null!;
    private long _atomicCounter;
    private readonly TagList _tags = new() { { "cache.name", "test-cache" } };
    
    /// <summary>
    /// Sets up the benchmark environment by initializing the meter and counter.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _meter = new Meter("BenchmarkMeter");
        _counter = _meter.CreateCounter<long>("test_counter");
        _atomicCounter = 0;
    }
    
    /// <summary>
    /// Cleans up the benchmark environment by disposing the meter.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _meter?.Dispose();
    }
    
    /// <summary>
    /// Baseline: No metric tracking
    /// </summary>
    [Benchmark(Baseline = true)]
    public long NoMetrics()
    {
        // Simulate some minimal work
        return 42;
    }
    
    /// <summary>
    /// Using System.Diagnostics.Metrics.Counter with tags
    /// </summary>
    [Benchmark]
    public void Counter_WithTags()
    {
        _counter.Add(1, _tags);
    }
    
    /// <summary>
    /// Using System.Diagnostics.Metrics.Counter without tags
    /// </summary>
    [Benchmark]
    public void Counter_NoTags()
    {
        _counter.Add(1);
    }
    
    /// <summary>
    /// Using Interlocked.Increment (atomic operation)
    /// </summary>
    [Benchmark]
    public long Interlocked_Increment()
    {
        return Interlocked.Increment(ref _atomicCounter);
    }
    
    /// <summary>
    /// Using Interlocked.Add (atomic operation)
    /// </summary>
    [Benchmark]
    public long Interlocked_Add()
    {
        return Interlocked.Add(ref _atomicCounter, 1);
    }
    
    /// <summary>
    /// Simulating the full cache hit scenario with Counter
    /// </summary>
    [Benchmark]
    public void CacheHit_WithCounter()
    {
        // Simulate cache lookup
        var hit = true;
        
        if (hit)
        {
            _counter.Add(1, _tags);
        }
    }
    
    /// <summary>
    /// Simulating the full cache hit scenario with Interlocked
    /// </summary>
    [Benchmark]
    public void CacheHit_WithInterlocked()
    {
        // Simulate cache lookup
        var hit = true;
        
        if (hit)
        {
            Interlocked.Increment(ref _atomicCounter);
        }
    }
    
    /// <summary>
    /// High contention scenario with Counter (multiple threads)
    /// </summary>
    [Benchmark]
    public void HighContention_Counter()
    {
        Parallel.For(0, 10, _ =>
        {
            _counter.Add(1, _tags);
        });
    }
    
    /// <summary>
    /// High contention scenario with Interlocked (multiple threads)
    /// </summary>
    [Benchmark]
    public void HighContention_Interlocked()
    {
        Parallel.For(0, 10, _ =>
        {
            Interlocked.Increment(ref _atomicCounter);
        });
    }
}

