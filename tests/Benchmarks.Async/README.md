# Async hit benchmarks: .NET 11 runtime async measurement

The library core is synchronous. Its consumers are not: the ASP.NET Core example
in this repo, and real services like it, put async facades and decorator layers
over the cache. On a hit, every one of those awaits completes synchronously, so
the async state machine is a measurable share of the per-hit cost.

.NET 11 runtime async moves async suspension into the runtime and removes much
of the compiler-generated state machine. It is opt-in per assembly with `<Features>runtime-async=on</Features>` and needs .NET 11. This
pair of projects measures what it buys on this repo's cache stack.

## Projects

- `Benchmarks.Async`: async facades over a raw `MemoryCache` and over
  `MeteredMemoryCache`, benchmarked on net11.0 with classic async.
- `../Benchmarks.Async.RuntimeAsync`: the identical source, linked, compiled
  with `runtime-async=on`. The flag is the only difference.

Both projects are deliberately outside the solution. The repo pins SDK 10 in
the root `global.json`. Each project folder carries its own `global.json`
that selects SDK 11. The pin is a floor: it allows release candidates and
rolls forward to later SDK 11 builds, including GA.

Run `dotnet` from inside the project folder so the nested `global.json` is
the one resolved. Every `.cs` file under `Benchmarks.Async` is linked into the
RuntimeAsync build, so a new file lands in both builds.

CI does not build or run these projects. Run `selfcheck` in both folders after
any change to `CacheImplementations` or to this source.

## Run

```sh
cd tests/Benchmarks.Async
dotnet run -c Release -- --filter '*'
dotnet bin/Release/net11.0/Benchmarks.Async.dll direct     # Stopwatch harness, 2M hits per method
dotnet bin/Release/net11.0/Benchmarks.Async.dll selfcheck  # Build flag and hit counter checks

cd ../Benchmarks.Async.RuntimeAsync
dotnet run -c Release -- --filter '*'
dotnet bin/Release/net11.0/Benchmarks.Async.RuntimeAsync.dll direct
dotnet bin/Release/net11.0/Benchmarks.Async.RuntimeAsync.dll selfcheck
```

Every mode first prints which build ran and whether runtime async is compiled
in. `selfcheck` fails when the compiled flag does not match the project, or
when a miss counts as a hit.

## Workloads

Each workload awaits 100 cache hits per call.

| Workload                 | Async layers | Cache                |
| ------------------------ | ------------ | -------------------- |
| `RawHitAsync`            | 1            | `MemoryCache`        |
| `LayeredRawHitAsync`     | 2            | `MemoryCache`        |
| `LayeredMeteredHitAsync` | 2            | `MeteredMemoryCache` |

`RawHitAsync` versus `LayeredRawHitAsync` isolates async depth. The second
layer also adds its hit counter, so depth and that increment stay bundled.
`LayeredRawHitAsync` versus `LayeredMeteredHitAsync` isolates the cache.
No `MeterListener` is attached, so the metered cache runs with its
instruments disabled.

The `direct` mode times the compiled assemblies end to end and bypasses
BenchmarkDotNet's in-process code generation, so what is measured is exactly
the compilation each project produced. It warms every method before timing
any of them. Without that, the first method timed runs partly
on tier-0 JIT code and reads slower than the methods after it.

## What the numbers said (2026-11-16, Linux x64 VM, .NET 11.0.0-rc.3)

Per cache hit, classic async versus runtime async on identical source. Direct
figures are the mean of three runs.

| Workload                 | BenchmarkDotNet              | Direct                       |
| ------------------------ | ---------------------------- | ---------------------------- |
| `RawHitAsync`            | 41.20 vs 36.17 ns (-5.0 ns)  | 44.60 vs 36.30 ns (-8.3 ns)  |
| `LayeredRawHitAsync`     | 53.70 vs 41.78 ns (-11.9 ns) | 56.86 vs 41.57 ns (-15.3 ns) |
| `LayeredMeteredHitAsync` | 61.68 vs 49.49 ns (-12.2 ns) | 60.50 vs 48.94 ns (-11.6 ns) |

Gains: one layer 12% (BDN) and 13 to 22% (direct runs). Two layers over the
raw cache 22% (BDN) and 23 to 30% (direct). Two layers over the metered cache
20% (BDN) and 18 to 20% (direct).

Allocations were zero in both compilations. Runtime async was faster in every
workload and every run. One async layer saves 5 to 8 ns per hit. Two layers
save 12 to 15 ns, so the saving grows with each async layer a hit crosses.
BenchmarkDotNet shows the same saving over either cache. The direct harness
shows a smaller saving over the metered cache, 11.6 ns versus 15.3 ns. The
cache effect is therefore inconclusive. The depth effect holds in both
harnesses. That layered shape is the decorator stack consumers build over
this library.
