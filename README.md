# memory-cache-solutions

High‑quality experimental patterns & decorators built on top of `IMemoryCache` ([Microsoft.Extensions.Caching.Memory](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory)) to address common performance and correctness concerns:

| Component | Purpose | Concurrency Control | Async Support | Extra Features |
|-----------|---------|---------------------|---------------|----------------|
| `CoalescingMemoryCache` | Drop‑in `IMemoryCache` decorator that coalesces concurrent cache misses (single‑flight) | `Lazy<Task<T>>` per key in a concurrent dictionary (removed after completion) | Yes (`GetOrCreateAsync`) | Works with any existing `IMemoryCache` usage; minimal allocation on hits |
| `MeteredMemoryCache` | Emits OpenTelemetry / .NET `System.Diagnostics.Metrics` counters for hits, misses, evictions | N/A (no single‑flight) | N/A (sync like base cache) | Counters: `cache_hits_total`, `cache_misses_total`, `cache_evictions_total{reason}` |
| `SingleFlightCache` | Stand‑alone helper ensuring only one concurrent async factory executes per key | Per‑key transient `SemaphoreSlim` | Yes | TTL & optional entry configuration delegate |
| `SingleFlightLazyCache` | Single‑flight via cached `Lazy<Task<T>>` entry (no external lock) | Publication semantics of `Lazy` | Yes | Simplest implementation; cancellation only affects awaiting caller |
| `GetOrCreateSwrAsync` (SWR extension) | Stale‑While‑Revalidate pattern (serve stale while one background refresh updates) | Interlocked flag in boxed state | Yes | Background refresh isolated from caller cancellation; resilience to refresh failures |

> These implementations favor clarity & demonstrable patterns over feature breadth. They are intentionally small and suitable as a starting point for production adaptation.

---

## Quick Start

Add the project (or copy the desired file) into your solution and reference it from your application. Example using the coalescing decorator with DI:

```csharp
builder.Services.AddMemoryCache();
builder.Services.Decorate<IMemoryCache>(inner => new CoalescingMemoryCache(inner, disposeInner: false));
```

Using the provided single‑flight helpers directly:

```csharp
var memory = new MemoryCache(new MemoryCacheOptions());
var singleFlight = new SingleFlightCache(memory);

int value = await singleFlight.GetOrCreateAsync(
	key: "expensive:data",
	ttl: TimeSpan.FromMinutes(5),
	factory: async () => await FetchExpensiveValueAsync());
```

Applying Stale‑While‑Revalidate (SWR) to serve stale data while refreshing in the background:

```csharp
var opts = new SwrOptions(
	Ttl:   TimeSpan.FromSeconds(30),   // fresh window
	Stale: TimeSpan.FromSeconds(120)); // additional stale window

var result = await cache.GetOrCreateSwrAsync(
	key: "user:profile:42",
	opt: opts,
	factory: ct => FetchProfileAsync(ct));
```

Recording metrics with `MeteredMemoryCache`:

```csharp
var meter = new Meter("app.cache");
var metered = new MeteredMemoryCache(new MemoryCache(new MemoryCacheOptions()), meter);

metered.Set("answer", 42);
if (metered.TryGet<int>("answer", out var v)) { /* use v */ }
```

Counters exposed:

* `cache_hits_total`
* `cache_misses_total`
* `cache_evictions_total` (tag: `reason` = `Expired|TokenExpired|Capacity|Removed|Replaced|...`)

Consume with `MeterListener`, OpenTelemetry Metrics SDK, or any compatible exporter.

---

## Implementation Details & Semantics

### CoalescingMemoryCache

* Decorator implementing `IMemoryCache` so it can transparently replace the default cache.
* On a miss, installs a `Lazy<Task<T>>` for the key inside an internal `ConcurrentDictionary` so only the *first* caller runs the factory.
* After completion (success or failure) the in‑flight entry is removed; a failure permits a subsequent retry.
* Hit path is identical cost to the underlying cache.

Usage (async factory with full `ICacheEntry` access):
```csharp
var value = await coalescing.GetOrCreateAsync("k", async entry => {
	entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
	return await LoadAsync();
});
```

### MeteredMemoryCache

* Adds minimal instrumentation overhead (~1 counter add per op) while preserving `IMemoryCache` API.
* Eviction metric is emitted from a post‑eviction callback automatically registered on each created entry.
* Includes convenience `TryGet<T>` & `GetOrCreate<T>` wrappers emitting structured counters.
* Use when you need visibility (hit ratio, churn) without adopting a full external caching layer.

### SingleFlightCache

* Externally manages in‑flight coordination using a transient `SemaphoreSlim` per *current* miss key.
* Lock dictionary remains bounded by current concurrent keys only (entries removed immediately after creation completes).
* You supply TTL and optional entry configuration delegate.
* Cancellation token is observed while waiting for the lock and during the factory call; cancellation of one waiter does not cancel other waiters unless they share the same token.

### SingleFlightLazyCache

* Stores a `Lazy<Task<T>>` inside the cache entry itself (`IMemoryCache.GetOrCreate`).
* No explicit locking; relies on `LazyThreadSafetyMode.ExecutionAndPublication` for safe single-flight semantics.
* Caller cancellation only affects the awaiting operation; the underlying task continues to completion so subsequent callers get the finished value.
* Simpler but you cannot directly supply custom `ICacheEntry` configuration per call beyond first creation (do it in the creation lambda).

### Stale‑While‑Revalidate Extensions (`GetOrCreateSwrAsync` + `SwrOptions`)

* Pattern: serve *fresh* until TTL; while *stale* (TTL elapsed but within `Ttl + Stale`) keep serving old value and trigger a *single* background refresh (non-blocking); after `Ttl + Stale` the entry is evicted and callers block for a new value.
* Background refresh failures are swallowed, leaving stale data until next attempt or ultimate expiration.
* Minimal state: boxed struct containing value, freshness timestamp, and an `int` flag for refresh state.
* Ideal for latency-sensitive endpoints (e.g., profile cards, feature flags) where slight staleness is preferable to request fan‑out.

Example timing diagram (`Ttl = 30s`, `Stale = 2m`):

```text
0s ─ fresh window ─ 30s ─ stale window (serving old + 1 refresh) ─ 150s eviction
```

---

## Choosing an Approach

| Scenario | Recommended |
|----------|------------|
| Prevent stampede for expensive async load integrated through existing `IMemoryCache` usage | `CoalescingMemoryCache` |
| Need metrics (hit ratio, eviction reasons) | `MeteredMemoryCache` (can stack with coalescing via multiple decorators) |
| Simple on-demand helper (not a decorator) with explicit TTL | `SingleFlightCache` |
| Simplest single-flight with minimal code & contention | `SingleFlightLazyCache` |
| Reduce tail latency by serving slightly stale data & refreshing in background | SWR extensions |

You can combine patterns: e.g., wrap the inner cache with metrics, then wrap that with coalescing for async factories.

---

## Concurrency, Cancellation & Failure Notes

| Component | Cancellation Behavior | Failure Behavior |
|-----------|-----------------------|------------------|
| CoalescingMemoryCache | Cancellation of the awaited task cancels only that caller; other awaiters continue. Factory exception propagates to all awaiters. | All awaiting callers observe the same exception; entry not cached; subsequent call retries. |
| SingleFlightCache | Token observed while acquiring lock & running factory. Canceling the currently executing factory aborts creation. | Exception propagates; next caller retries. |
| SingleFlightLazyCache | Cancellation only affects awaiting caller; underlying `Task` continues. | Exception cached inside the `Lazy<Task>`; all callers observe it; entry removed on next attempt due to expiration or manual removal (adapt as needed). |
| SWR | Foreground miss uses caller token; background refresh ignores caller tokens. | Background exceptions swallowed (stale value served). |
| MeteredMemoryCache | N/A (no async). | Eviction reasons recorded regardless. |

---

## Benchmarks

Benchmarks (BenchmarkDotNet) included under `tests/Benchmarks` compare relative overhead of wrappers. To run:

```bash
dotnet run -c Release -p tests/Benchmarks/Benchmarks.csproj
```

Interpretation guidance:

* Hit paths for single‑flight variants should be close to raw cache once warm.
* Coalescing and SingleFlight variants add overhead only during contested cold starts.
* SWR introduces minimal overhead on hits; background refresh cost is off critical path.

> Always benchmark within your workload; microbenchmarks do not capture memory pressure, GC, or production contention levels.

---

## Benchmark Regression Gate (BenchGate)

The repository includes a lightweight regression gate comparing the latest BenchmarkDotNet run against committed baselines.

Quick local workflow:

```powershell
dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *SingleFlight*
Copy-Item BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json BenchmarkDotNet.Artifacts/results/current.json
dotnet run -c Release --project tools/BenchGate/BenchGate.csproj -- benchmarks/baseline/CacheBenchmarks.json BenchmarkDotNet.Artifacts/results/current.json
```

Thresholds (defaults):

* Time regression: >3% AND >5 ns absolute
* Allocation regression: increase >16 B AND >3%

Update baseline only after a verified improvement:

```powershell
Copy-Item BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json benchmarks/baseline/CacheBenchmarks.json
git add benchmarks/baseline/CacheBenchmarks.json
git commit -m "chore(bench): update CacheBenchmarks baseline" -m "Include before/after metrics table"
```

CI runs the gate automatically (see `.github/workflows/ci.yml`).

### BenchGate Regression Gating

BenchGate compares the latest BenchmarkDotNet full JSON output(s) against committed baselines under `benchmarks/baseline/`.

Supported CLI flags:
- `--suite=<SuiteName>`: Explicit suite name if not inferrable.
- `--time-threshold=<double>`: Relative mean time regression guard (default 0.03).
- `--alloc-threshold-bytes=<int>`: Absolute allocation regression guard (default 16).
- `--alloc-threshold-pct=<double>`: Relative allocation regression guard (default 0.03).
- `--sigma-mult=<double>`: Sigma multiplier for statistical significance (default 2.0).
- `--no-sigma`: Disable significance filtering (treat all deltas as significant subject to thresholds).

Per‑OS baseline resolution order when first argument is a directory:
1. `<Suite>.<os>.<arch>.json`
2. `<Suite>.<os>.json`
3. `<Suite>.json`

Current baselines (Windows):
- `CacheBenchmarks.windows-latest.json`
- `ContentionBenchmarks.windows-latest.json`

Add additional OS baselines by copying the corresponding `*-report-full.json` into the baseline directory using the naming convention above.

Evidence & Process requirements are described in `.github/copilot-instructions.md` Sections 12–14.

---

## Extensibility Ideas

* Add jitter to SWR refresh scheduling to avoid synchronized refresh bursts across processes.
* Enrich metrics (e.g., object size, latency histogram for factory execution).
* Add negative caching (cache specific failures briefly) if upstream calls are very costly.
* Provide a multi-layer (L1 in-memory + L2 distributed) single-flight composition.

---

## Testing

Unit tests cover: single-flight coalescing correctness under concurrency, TTL expiry, background refresh semantics, metrics emission, and cancellation behavior. See the `tests/Unit` directory for usage patterns.

---

## License

MIT (see `LICENSE.txt`).

---

## Disclaimer

These are illustrative implementations. Review thread-safety, memory usage, eviction policies, and failure handling for your production context (high cardinality keys, very large payloads, process restarts, etc.).
