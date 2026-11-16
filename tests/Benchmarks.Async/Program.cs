using System.Diagnostics;
using BenchmarkDotNet.Running;
using Benchmarks.Async;

if (args.Contains("direct"))
{
    // Direct timing mode: runs the compiled adapter assemblies end to end with a
    // Stopwatch, bypassing BenchmarkDotNet's in-process code generation, so the
    // runtime-async compilation of this assembly is exactly what gets measured.
    var benchmarks = new AsyncCacheBenchmarks();
    benchmarks.Setup();

    const int calls = 20_000; // Each call awaits 100 cache hits.
    foreach ((string name, Func<ValueTask<int>> method) in new (string, Func<ValueTask<int>>)[]
    {
        ("RawHitAsync", benchmarks.RawHitAsync),
        ("LayeredMeteredHitAsync", benchmarks.LayeredMeteredHitAsync),
    })
    {
        for (int i = 0; i < 1_000; i++)
        {
            await method();
        }

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < calls; i++)
        {
            await method();
        }

        stopwatch.Stop();
        double nsPerHit = stopwatch.Elapsed.TotalNanoseconds / (calls * 100.0);
        Console.WriteLine($"{name}: {nsPerHit:F2} ns/hit over {calls * 100:N0} hits");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
