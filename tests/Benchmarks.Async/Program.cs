using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Running;
using Benchmarks.Async;
using Microsoft.Extensions.Caching.Memory;

// Identify the build under test, so output from the two projects cannot be confused.
string assemblyName = typeof(AsyncCacheAdapter).Assembly.GetName().Name!;
bool expectRuntimeAsync = assemblyName.EndsWith(".RuntimeAsync", StringComparison.Ordinal);
bool compiledRuntimeAsync = typeof(AsyncCacheAdapter)
    .GetMethod(nameof(AsyncCacheAdapter.GetAsync))!
    .MethodImplementationFlags
    .HasFlag(MethodImplAttributes.Async);
Console.WriteLine($"build: {assemblyName}, {RuntimeInformation.FrameworkDescription}, runtime-async={compiledRuntimeAsync}");

if (args.Contains("selfcheck"))
{
    // The runtime-async flag must have taken effect in exactly the RuntimeAsync build.
    bool flagMatches = compiledRuntimeAsync == expectRuntimeAsync;
    Console.WriteLine($"selfcheck: expected runtime-async={expectRuntimeAsync} compiled runtime-async={compiledRuntimeAsync} => {(flagMatches ? "PASS" : "FAIL")}");

    // Regression check for the layered hit counter: a miss must not count as a hit.
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var layered = new LayeredAsyncCacheAdapter(new AsyncCacheAdapter(cache));

    CacheLookup miss = await layered.GetAsync("a");
    long hitsAfterMiss = layered.Hits;
    CacheLookup hit = await layered.GetAsync("a");
    long hitsAfterHit = layered.Hits;

    bool countsHits = !miss.IsHit && hitsAfterMiss == 0
        && hit.IsHit && hitsAfterHit == 1
        && hit.Value == miss.Value;
    Console.WriteLine($"selfcheck: miss.IsHit={miss.IsHit} hitsAfterMiss={hitsAfterMiss} hit.IsHit={hit.IsHit} hitsAfterHit={hitsAfterHit} => {(countsHits ? "PASS" : "FAIL")}");
    return flagMatches && countsHits ? 0 : 1;
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

        const int calls = 20_000;
        const double hitsPerCall = AsyncCacheBenchmarks.HitsPerInvoke;
        (string Name, Func<ValueTask<int>> Method)[] methods =
        [
            ("RawHitAsync", benchmarks.RawHitAsync),
            ("LayeredRawHitAsync", benchmarks.LayeredRawHitAsync),
            ("LayeredMeteredHitAsync", benchmarks.LayeredMeteredHitAsync),
        ];

        // Warm every method before timing any of them. Without this, the first
        // method timed runs partly on tier-0 JIT code and reads slower than the
        // methods after it.
        for (int round = 0; round < 3; round++)
        {
            foreach ((_, Func<ValueTask<int>> method) in methods)
            {
                for (int i = 0; i < 5_000; i++)
                {
                    await method();
                }
            }
        }

        foreach ((string name, Func<ValueTask<int>> method) in methods)
        {
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < calls; i++)
            {
                await method();
            }

            stopwatch.Stop();
            double nsPerHit = stopwatch.Elapsed.TotalNanoseconds / (calls * hitsPerCall);
            Console.WriteLine($"{name}: {nsPerHit:F2} ns/hit over {calls * hitsPerCall:N0} hits");
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
