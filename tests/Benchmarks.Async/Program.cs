using System.Diagnostics;
using BenchmarkDotNet.Running;
using Benchmarks.Async;
using Microsoft.Extensions.Caching.Memory;

if (args.Contains("selfcheck"))
{
    // Regression check for the layered hit counter: a miss must not count as a hit.
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var layered = new LayeredAsyncCacheAdapter(new AsyncCacheAdapter(cache));

    CacheLookup miss = await layered.GetAsync("a");
    long hitsAfterMiss = layered.Hits;
    CacheLookup hit = await layered.GetAsync("a");
    long hitsAfterHit = layered.Hits;

    bool passed = !miss.IsHit && hitsAfterMiss == 0
        && hit.IsHit && hitsAfterHit == 1
        && hit.Value == miss.Value;
    Console.WriteLine($"selfcheck: miss.IsHit={miss.IsHit} hitsAfterMiss={hitsAfterMiss} hit.IsHit={hit.IsHit} hitsAfterHit={hitsAfterHit} => {(passed ? "PASS" : "FAIL")}");
    return passed ? 0 : 1;
}

if (args.Contains("direct"))
{
    // Direct timing mode: runs the compiled adapter assemblies end to end with a
    // Stopwatch, bypassing BenchmarkDotNet's in-process code generation, so the
    // runtime-async compilation of this assembly is exactly what gets measured.
    var benchmarks = new AsyncCacheBenchmarks();
    try
    {
        benchmarks.Setup();

        const int calls = 20_000; // Each call awaits 100 cache hits.
        (string Name, Func<ValueTask<int>> Method)[] methods =
        [
            ("RawHitAsync", benchmarks.RawHitAsync),
            ("LayeredRawHitAsync", benchmarks.LayeredRawHitAsync),
            ("LayeredMeteredHitAsync", benchmarks.LayeredMeteredHitAsync),
        ];

        // Warm every method before timing any of them, then pause so background
        // tier-1 compilation finishes. Without this, the first method timed runs
        // partly on tier-0 code and reads slower than the methods after it.
        for (int round = 0; round < 3; round++)
        {
            foreach ((_, Func<ValueTask<int>> method) in methods)
            {
                for (int i = 0; i < 5_000; i++)
                {
                    await method();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        foreach ((string name, Func<ValueTask<int>> method) in methods)
        {
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < calls; i++)
            {
                await method();
            }

            stopwatch.Stop();
            double nsPerHit = stopwatch.Elapsed.TotalNanoseconds / (calls * 100.0);
            Console.WriteLine($"{name}: {nsPerHit:F2} ns/hit over {calls * 100:N0} hits");
        }
    }
    finally
    {
        benchmarks.Cleanup();
    }

    return 0;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
