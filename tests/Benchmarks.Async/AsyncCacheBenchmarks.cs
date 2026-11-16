using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Unit;

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

    private Meter _meter = null!;
    private MemoryCache _rawCache = null!;
    private MemoryCache _layeredRawCache = null!;
    private MeteredMemoryCache _meteredCache = null!;

    private AsyncCacheAdapter _rawAdapter = null!;
    private LayeredAsyncCacheAdapter _layeredRawAdapter = null!;
    private LayeredAsyncCacheAdapter _layeredMeteredAdapter = null!;

    /// <summary>
    /// Builds the caches and adapters with the test key pre-populated, so every
    /// benchmarked call is a hit.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _meter = new Meter(SharedUtilities.GetUniqueMeterName("benchmarks.async"));

        _rawCache = new MemoryCache(new MemoryCacheOptions());
        _rawCache.Set(TestKey, TestValue);
        _rawAdapter = new AsyncCacheAdapter(_rawCache);

        _layeredRawCache = new MemoryCache(new MemoryCacheOptions());
        _layeredRawCache.Set(TestKey, TestValue);
        _layeredRawAdapter = new LayeredAsyncCacheAdapter(new AsyncCacheAdapter(_layeredRawCache));

        var innerCache = new MemoryCache(new MemoryCacheOptions());
        _meteredCache = new MeteredMemoryCache(innerCache, _meter, SharedUtilities.GetUniqueCacheName("benchmark-cache"), disposeInner: true);
        _meteredCache.Set(TestKey, TestValue);
        _layeredMeteredAdapter = new LayeredAsyncCacheAdapter(new AsyncCacheAdapter(_meteredCache));
    }

    /// <summary>
    /// Disposes the caches and the meter created by <see cref="Setup"/>.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _meteredCache?.Dispose();
        _layeredRawCache?.Dispose();
        _rawCache?.Dispose();
        _meter?.Dispose();
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
            CacheLookup lookup = await _rawAdapter.GetAsync(TestKey).ConfigureAwait(false);
            total += lookup.Value?.Length ?? 0;
        }

        return total;
    }

    /// <summary>
    /// Awaits 100 cache hits through two async layers over a raw MemoryCache.
    /// This is the depth control: it differs from <see cref="RawHitAsync"/> only by
    /// the second async layer and its hit counter.
    /// </summary>
    /// <returns>The summed value lengths, so the loop cannot be optimized away.</returns>
    [Benchmark(OperationsPerInvoke = HitsPerInvoke)]
    public async ValueTask<int> LayeredRawHitAsync()
    {
        int total = 0;
        for (int i = 0; i < HitsPerInvoke; i++)
        {
            CacheLookup lookup = await _layeredRawAdapter.GetAsync(TestKey).ConfigureAwait(false);
            total += lookup.Value?.Length ?? 0;
        }

        return total;
    }

    /// <summary>
    /// Awaits 100 cache hits through two async layers over a MeteredMemoryCache.
    /// It differs from <see cref="LayeredRawHitAsync"/> only by the cache implementation.
    /// </summary>
    /// <returns>The summed value lengths, so the loop cannot be optimized away.</returns>
    [Benchmark(OperationsPerInvoke = HitsPerInvoke)]
    public async ValueTask<int> LayeredMeteredHitAsync()
    {
        int total = 0;
        for (int i = 0; i < HitsPerInvoke; i++)
        {
            CacheLookup lookup = await _layeredMeteredAdapter.GetAsync(TestKey).ConfigureAwait(false);
            total += lookup.Value?.Length ?? 0;
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
