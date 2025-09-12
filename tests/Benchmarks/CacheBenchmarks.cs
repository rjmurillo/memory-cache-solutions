using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

namespace Benchmarks;

/// <summary>
/// Benchmarks comparing performance overhead of named vs unnamed MeteredMemoryCache instances.
/// Tests the impact of dimensional metrics (cache.name tags) on cache operation performance.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[JsonExporter]
public class CacheBenchmarks
{
    private IMemoryCache _rawCache = null!;
    private IMemoryCache _meteredUnnamedCache = null!;
    private IMemoryCache _meteredNamedCache = null!;
    private Meter _meter = null!;

    private const string TestKey = "test-key";
    private const string TestValue = "test-value-with-some-length-to-simulate-realistic-cache-entries";
    private const string CacheName = "benchmark-cache";

    [GlobalSetup]
    public void Setup()
    {
        // Raw MemoryCache for baseline comparison
        _rawCache = new MemoryCache(new MemoryCacheOptions());

        // Meter for MeteredMemoryCache instances
        _meter = new Meter("BenchmarkMeter");

        // MeteredMemoryCache without cache name (unnamed)
        var unnamedInnerCache = new MemoryCache(new MemoryCacheOptions());
        _meteredUnnamedCache = new MeteredMemoryCache(unnamedInnerCache, _meter);

        // MeteredMemoryCache with cache name (named/dimensional metrics)
        var namedInnerCache = new MemoryCache(new MemoryCacheOptions());
        _meteredNamedCache = new MeteredMemoryCache(namedInnerCache, _meter, CacheName);

        // Pre-populate caches for hit scenarios
        _rawCache.Set(TestKey, TestValue);
        _meteredUnnamedCache.Set(TestKey, TestValue);
        _meteredNamedCache.Set(TestKey, TestValue);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rawCache?.Dispose();
        _meteredUnnamedCache?.Dispose();
        _meteredNamedCache?.Dispose();
        _meter?.Dispose();
    }

    #region Cache Hit Benchmarks

    /// <summary>
    /// Baseline: Raw MemoryCache hit performance without any metrics overhead.
    /// </summary>
    [Benchmark(Baseline = true)]
    public object? RawCache_Hit()
    {
        return _rawCache.Get(TestKey);
    }

    /// <summary>
    /// MeteredMemoryCache hit performance without cache name (no dimensional tags).
    /// Tests overhead of basic metric emission without tag processing.
    /// </summary>
    [Benchmark]
    public object? MeteredCache_Unnamed_Hit()
    {
        return _meteredUnnamedCache.Get(TestKey);
    }

    /// <summary>
    /// MeteredMemoryCache hit performance with cache name (dimensional tags).
    /// Tests additional overhead of tag processing and dimensional metrics.
    /// </summary>
    [Benchmark]
    public object? MeteredCache_Named_Hit()
    {
        return _meteredNamedCache.Get(TestKey);
    }

    #endregion

    #region Cache Miss Benchmarks

    /// <summary>
    /// Baseline: Raw MemoryCache miss performance without any metrics overhead.
    /// </summary>
    [Benchmark]
    public object? RawCache_Miss()
    {
        return _rawCache.Get("nonexistent-key");
    }

    /// <summary>
    /// MeteredMemoryCache miss performance without cache name.
    /// Tests metric emission overhead on cache misses.
    /// </summary>
    [Benchmark]
    public object? MeteredCache_Unnamed_Miss()
    {
        return _meteredUnnamedCache.Get("nonexistent-key");
    }

    /// <summary>
    /// MeteredMemoryCache miss performance with cache name.
    /// Tests dimensional metric overhead on cache misses.
    /// </summary>
    [Benchmark]
    public object? MeteredCache_Named_Miss()
    {
        return _meteredNamedCache.Get("nonexistent-key");
    }

    #endregion

    #region Cache Set Benchmarks

    private int _setCounter = 0;

    /// <summary>
    /// Baseline: Raw MemoryCache set performance without any metrics overhead.
    /// </summary>
    [Benchmark]
    public void RawCache_Set()
    {
        _rawCache.Set($"set-key-{++_setCounter}", TestValue);
    }

    /// <summary>
    /// MeteredMemoryCache set performance without cache name.
    /// Tests eviction callback registration overhead without dimensional tags.
    /// </summary>
    [Benchmark]
    public void MeteredCache_Unnamed_Set()
    {
        _meteredUnnamedCache.Set($"set-key-{++_setCounter}", TestValue);
    }

    /// <summary>
    /// MeteredMemoryCache set performance with cache name.
    /// Tests eviction callback registration overhead with dimensional tags.
    /// </summary>
    [Benchmark]
    public void MeteredCache_Named_Set()
    {
        _meteredNamedCache.Set($"set-key-{++_setCounter}", TestValue);
    }

    #endregion

    #region TryGetValue Benchmarks

    /// <summary>
    /// Baseline: Raw MemoryCache TryGetValue hit performance.
    /// </summary>
    [Benchmark]
    public bool RawCache_TryGetValue_Hit()
    {
        return _rawCache.TryGetValue(TestKey, out _);
    }

    /// <summary>
    /// MeteredMemoryCache TryGetValue hit performance without cache name.
    /// </summary>
    [Benchmark]
    public bool MeteredCache_Unnamed_TryGetValue_Hit()
    {
        return _meteredUnnamedCache.TryGetValue(TestKey, out _);
    }

    /// <summary>
    /// MeteredMemoryCache TryGetValue hit performance with cache name.
    /// </summary>
    [Benchmark]
    public bool MeteredCache_Named_TryGetValue_Hit()
    {
        return _meteredNamedCache.TryGetValue(TestKey, out _);
    }

    /// <summary>
    /// Baseline: Raw MemoryCache TryGetValue miss performance.
    /// </summary>
    [Benchmark]
    public bool RawCache_TryGetValue_Miss()
    {
        return _rawCache.TryGetValue("nonexistent-key", out _);
    }

    /// <summary>
    /// MeteredMemoryCache TryGetValue miss performance without cache name.
    /// </summary>
    [Benchmark]
    public bool MeteredCache_Unnamed_TryGetValue_Miss()
    {
        return _meteredUnnamedCache.TryGetValue("nonexistent-key", out _);
    }

    /// <summary>
    /// MeteredMemoryCache TryGetValue miss performance with cache name.
    /// </summary>
    [Benchmark]
    public bool MeteredCache_Named_TryGetValue_Miss()
    {
        return _meteredNamedCache.TryGetValue("nonexistent-key", out _);
    }

    #endregion

    #region CreateEntry Benchmarks

    private int _createCounter = 0;

    /// <summary>
    /// Baseline: Raw MemoryCache CreateEntry performance.
    /// </summary>
    [Benchmark]
    public void RawCache_CreateEntry()
    {
        using var entry = _rawCache.CreateEntry($"create-key-{++_createCounter}");
        entry.Value = TestValue;
    }

    /// <summary>
    /// MeteredMemoryCache CreateEntry performance without cache name.
    /// Tests eviction callback overhead during entry creation.
    /// </summary>
    [Benchmark]
    public void MeteredCache_Unnamed_CreateEntry()
    {
        using var entry = _meteredUnnamedCache.CreateEntry($"create-key-{++_createCounter}");
        entry.Value = TestValue;
    }

    /// <summary>
    /// MeteredMemoryCache CreateEntry performance with cache name.
    /// Tests dimensional eviction callback overhead during entry creation.
    /// </summary>
    [Benchmark]
    public void MeteredCache_Named_CreateEntry()
    {
        using var entry = _meteredNamedCache.CreateEntry($"create-key-{++_createCounter}");
        entry.Value = TestValue;
    }

    #endregion
}

/// <summary>
/// BenchmarkDotNet configuration for cache performance testing.
/// Configured for CI-friendly execution with reduced memory usage while maintaining accuracy.
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        // JSON output will be generated by default
        AddDiagnoser(MemoryDiagnoser.Default);
        AddDiagnoser(ThreadingDiagnoser.Default);

        // CI-friendly job configuration: reduced memory usage, faster execution
        var ciJob = Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)  // In-process execution
            .WithWarmupCount(3)           // Reduced from default 6-15
            .WithIterationCount(10)       // Reduced from default 15-100  
            .WithInvocationCount(16384)   // Reduced from default 1M+
            .WithUnrollFactor(1)          // Minimal unrolling
            .WithId("CIJob");
        AddJob(ciJob);
    }
}
