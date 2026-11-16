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
the root `global.json`; each project folder carries its own `global.json`
pinning SDK 11. Run `dotnet` from inside the project folder so the nested
`global.json` is the one resolved.

## Run

```sh
cd tests/Benchmarks.Async
dotnet run -c Release -- --filter '*'
dotnet bin/Release/net11.0/Benchmarks.Async.dll direct   # Stopwatch harness, 2M hits per method

cd ../Benchmarks.Async.RuntimeAsync
dotnet run -c Release -- --filter '*'
dotnet bin/Release/net11.0/Benchmarks.Async.RuntimeAsync.dll direct
```

The `direct` mode times the compiled assemblies end to end and bypasses
BenchmarkDotNet's in-process code generation, so what is measured is exactly
the compilation each project produced.

## What the numbers said (2026-11-15, shared VM, treat absolutes with care)

Per cache hit, runtime async versus classic async on identical source:

| Workload | BenchmarkDotNet | Direct run 1 | Direct run 2 |
| --- | --- | --- | --- |
| One async layer over raw MemoryCache | no gain (noise) | 18% faster | no gain |
| Two async layers over MeteredMemoryCache | 7% faster | 35% faster | 30% faster |

Allocations were zero in both compilations. The pattern is consistent across
every run: a single thin facade gains nothing measurable, and the gain appears
and grows with the number of async layers a hit crosses. That is the decorator
stack consumers actually build over this library.
