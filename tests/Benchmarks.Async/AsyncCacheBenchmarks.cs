using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

namespace Benchmarks.Async;

/// <summary>
/// Measures the cost of awaiting through async cache facades when every call is a
/// cache hit and completes synchronously. This is the workload .NET 11 runtime
/// async targets: the lookup is nanoseconds, so the async state machine is the
/// dominant cost. Run the same benchmarks from Benchmarks.Async.RuntimeAsync,
/// which compiles this exact source with runtime-async=on, to isolate the effect.
/// </summary>
[Config(typeof(AsyncBenchmarkConfig))]
[MemoryDiagnoser]
public class AsyncCacheBenchmarks
{
    private const string TestKey = "test-key";
    private const string TestValue = "test-value-with-some-length-to-simulate-realistic-cache-entries";
    private const int HitsPerInvoke = 100;

    private readonly Meter _meter = new("BenchmarkMeter");

    private AsyncCacheAdapter _rawAdapter = null!;
    private LayeredAsyncCacheAdapter _layeredAdapter = null!;

    /// <summary>
    /// Builds the caches and adapters with the test key pre-populated, so every
    /// benchmarked call is a hit.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var rawCache = new MemoryCache(new MemoryCacheOptions());
        rawCache.Set(TestKey, TestValue);
        _rawAdapter = new AsyncCacheAdapter(rawCache);

        var innerCache = new MemoryCache(new MemoryCacheOptions());
        var meteredCache = new MeteredMemoryCache(innerCache, _meter, "benchmark-cache", disposeInner: true);
        meteredCache.Set(TestKey, TestValue);
        _layeredAdapter = new LayeredAsyncCacheAdapter(new AsyncCacheAdapter(meteredCache));
    }

    /// <summary>
    /// Awaits 100 cache hits through one async layer over a raw MemoryCache.
    /// </summary>
    /// <returns>The summed value lengths, so the loop cannot be optimized away.</returns>
    [Benchmark(OperationsPerInvoke = HitsPerInvoke)]
    public async ValueTask<int> RawHitAsync()
    {
        int total = 0;
        for (int i = 0; i < HitsPerInvoke; i++)
        {
            string? value = await _rawAdapter.GetAsync(TestKey).ConfigureAwait(false);
            total += value?.Length ?? 0;
        }

        return total;
    }

    /// <summary>
    /// Awaits 100 cache hits through two async layers over a MeteredMemoryCache.
    /// </summary>
    /// <returns>The summed value lengths, so the loop cannot be optimized away.</returns>
    [Benchmark(OperationsPerInvoke = HitsPerInvoke)]
    public async ValueTask<int> LayeredMeteredHitAsync()
    {
        int total = 0;
        for (int i = 0; i < HitsPerInvoke; i++)
        {
            string? value = await _layeredAdapter.GetAsync(TestKey).ConfigureAwait(false);
            total += value?.Length ?? 0;
        }

        return total;
    }
}

/// <summary>
/// BenchmarkDotNet configuration for the async hit benchmarks.
/// Mirrors the CI-friendly in-process setup of the main Benchmarks project.
/// </summary>
public class AsyncBenchmarkConfig : ManualConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncBenchmarkConfig"/> class.
    /// </summary>
    public AsyncBenchmarkConfig()
    {
        AddExporter(MarkdownExporter.GitHub);
        AddDiagnoser(MemoryDiagnoser.Default);

        var job = Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithId("Net11Job");
        AddJob(job);
    }
}
