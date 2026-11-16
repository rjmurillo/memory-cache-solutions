# Async hit benchmarks: .NET 11 runtime async measurement

The library core is synchronous. Its consumers are not: the ASP.NET Core example
in this repo, and real services like it, put async facades and decorator layers
over the cache. On a hit, every one of those awaits completes synchronously, so
the async state machine is a measurable share of the per-hit cost.

.NET 11 runtime async removes most of that state machine cost. It is opt-in per
assembly with `<Features>runtime-async=on</Features>` and needs .NET 11. This
pair of projects measures what it buys on this repo's cache stack.

## Projects

- `Benchmarks.Async`: async facades over a raw `MemoryCache` and over
  `MeteredMemoryCache`, benchmarked on net11.0 with classic async.
- `../Benchmarks.Async.RuntimeAsync`: the identical source, linked, compiled
  with `runtime-async=on`. The flag is the only difference.

Both projects are deliberately outside the solution. The repo pins SDK 10 in
the root `global.json`. Each project folder carries its own `global.json`
that selects SDK 11, allowing release candidates and rolling forward to GA. Run `dotnet` from inside the project folder so the nested
`global.json` is the one resolved.

## Run

```sh
cd tests/Benchmarks.Async
dotnet run -c Release -- --filter '*'
dotnet bin/Release/net11.0/Benchmarks.Async.dll direct     # Stopwatch harness, 2M hits per method
dotnet bin/Release/net11.0/Benchmarks.Async.dll selfcheck  # Miss-then-hit check of the hit counter

cd ../Benchmarks.Async.RuntimeAsync
dotnet run -c Release -- --filter '*'
dotnet bin/Release/net11.0/Benchmarks.Async.RuntimeAsync.dll direct
```

## Workloads

Each workload awaits 100 cache hits per call. Each pair differs by one factor.

| Workload                 | Async layers | Cache                |
| ------------------------ | ------------ | -------------------- |
| `RawHitAsync`            | 1            | `MemoryCache`        |
| `LayeredRawHitAsync`     | 2            | `MemoryCache`        |
| `LayeredMeteredHitAsync` | 2            | `MeteredMemoryCache` |

`RawHitAsync` versus `LayeredRawHitAsync` isolates async depth. The second
layer also adds its hit counter, so depth and that increment stay bundled.
`LayeredRawHitAsync` versus `LayeredMeteredHitAsync` isolates the cache.

The `direct` mode times the compiled assemblies end to end and bypasses
BenchmarkDotNet's in-process code generation, so what is measured is exactly
the compilation each project produced. It warms every method, then pauses,
before timing any of them. Without that, the first method timed runs partly
on tier-0 JIT code and reads slower than the methods after it.

## What the numbers said (2026-11-16, Linux x64 VM, SDK 11.0.100-rc.3)

Per cache hit, classic async versus runtime async on identical source:

| Workload                 | BenchmarkDotNet   | Direct (3 runs, mean) | Gain                          |
| ------------------------ | ----------------- | --------------------- | ----------------------------- |
| `RawHitAsync`            | 41.35 vs 35.84 ns | 45.88 vs 38.71 ns     | 13% (BDN), 15 to 16% (direct) |
| `LayeredRawHitAsync`     | 52.62 vs 41.36 ns | 58.68 vs 45.21 ns     | 21% (BDN), 22 to 25% (direct) |
| `LayeredMeteredHitAsync` | 59.76 vs 49.32 ns | 59.91 vs 47.17 ns     | 17% (BDN), 19 to 24% (direct) |

Allocations were zero in both compilations. Runtime async was faster in every
workload and every run. A single thin facade saves about 5.5 ns per hit. Two
layers save about 10 to 11 ns, roughly one saving per async layer. The cache
implementation does not change the saving. The gain grows with the number of
async layers a hit crosses, which is the decorator stack consumers build over
this library.
