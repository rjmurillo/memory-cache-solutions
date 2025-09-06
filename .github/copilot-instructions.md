# Copilot / Automation Workflow Guidance

Start every change by opening and reading this file (.github/copilot-instructions.md).

This repository uses an automated, performance-focused workflow for cache primitives. Follow these steps when proposing or implementing changes (manually or via Copilot / other AI assistants):

## 1. Scope & Atomicity
- Make ONE logical optimization or feature per commit ("atomic").
- Do not mix refactors with behavior changes unless required.
- Use Conventional Commit messages (examples below).

## 2. Change Categories (Examples)
| Type | Prefix | Example |
|------|--------|---------|
| Performance improvement | perf | `perf(single-flight-lazy): store Task<T> directly to remove Lazy allocation` |
| Feature | feat | `feat(coalescing-cache): add TryGetOrCreateAsync with created flag` |
| Refactor (no behavior change) | refactor | `refactor(metered): extract metric emission helper` |
| Tests | test | `test(single-flight): add cancellation scenario` |
| Docs | docs | `docs: add copilot workflow instructions` |
| Benchmark infra | chore | `chore(bench): parametrize contention levels` |

## 3. Standard Implementation Steps
1. Benchmark (baseline) the specific target(s) you expect to impact BEFORE editing code.
   - If unsure which, run ALL benchmarks:
     ```bash
     dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *
     ```
   - Targeted examples:
     ```bash
     dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *SingleFlightLazy*
     dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *Coalescing_Contention*
     ```
   - Save the pre-change summary (copy to commit body later).
2. Make the code change.
3. Run format & build:
   ```bash
   dotnet format
   dotnet build -c Release
   ```
4. Run tests:
   ```bash
   dotnet test -c Release --no-build
   ```
5. Re-run exactly the SAME benchmark filters as baseline (plus broader set if risk of spillover).
6. Compare: Mean (ns), Allocated (B), Gen0 (and higher if emitted), and the defined Contention Metrics (see "Contention Metrics Definition" below). If any unrelated core scenario regresses >3% in mean time OR contention metric thresholds are exceeded OR allocations increase without clear justification, FIX or ABORT before committing.
7. If perf change: ensure 100% test coverage of new branches/logic (add tests if necessary).
8. Update docs / comments impacted.
9. Craft commit message including before/after table (see template). DO NOT COMMIT perf-affecting change without numbers.
10. If you accidentally committed without numbers, immediately produce an A/B benchmark and either amend commit or add a corrective docs commit with metrics.

### Contention Metrics Definition
"Contention Metrics" are a fixed, explicitly collected set of concurrency and stall indicators you MUST baseline and re-measure for any change that can alter synchronization, scheduling, or allocation pressure. They are normalized per 1,000 logical benchmark operations (unless already emitted as a cumulative count for the full run). Add new metrics (with name, source, collection command) to the table below whenever a benchmark depends on them.

| Metric | What It Represents | Source / How Collected | Unit & Normalization | Threshold Trigger (regression) |
|--------|--------------------|------------------------|----------------------|---------------------------------|
| LockContentionCount | # of monitor/lock acquisitions that experienced contention | BenchmarkDotNet ThreadingDiagnoser (`[ThreadingDiagnoser]`) | Raw count per 1,000 ops | > +5 absolute per 1k ops AND > +3% relative vs baseline |
| MonitorWaitCount | # times threads waited (Monitor.Enter blocking) | ThreadingDiagnoser | Raw count per 1,000 ops | > +3% relative AND delta ≥ 2 per 1k ops |
| AvgLockWaitTimeNs | Average time a contended lock waited before acquisition | Derived: (TotalLockWaitTime / LockContentionCount); collect via EventPipe: `dotnet-trace collect --providers System.Runtime:0x4:5` then aggregate events `ContentionStop` | Nanoseconds (ns) | > +3% relative OR > +50 ns absolute |
| ThreadPoolQueueLength (peak) | Maximum queued ThreadPool work items (backpressure indicator) | `dotnet-counters monitor --refresh-interval 1 --counters System.Runtime` (counter: `threadpool-queue-length`) | Peak value over benchmark run | > +10% relative and absolute increase ≥ 2 |
| ThreadPoolCompletedItemsDelta | Change in completed items per second (throughput proxy) | `dotnet-counters` (`threadpool-completed-items-count`) sample start/end and divide by elapsed seconds | Items/sec | > -3% (drop) when time also regresses OR > -5% alone |
| GCGen0/1/2 Counts | # of GCs per generation | BDN default columns / `dotnet-counters` (`gen-0-gc-count`, etc.) | Count per full benchmark run | Any increase causing time or contention regression; > +5% counts triggers review |
| GCPauseTimeTotalMs | Sum of GC pause durations | `dotnet-trace` / PerfView (events: `GCStop`, `GCSuspendEE`, `GCRestartEE`) aggregated | Milliseconds per full run | > +3% relative AND > 0.5 ms absolute |
| AllocationRateBps (optional) | Allocation bytes per second (indirect contention via GC pressure) | `dotnet-counters` (`alloc-rate`) | Bytes/sec | > +3% with no functional justification |

Collection Procedure:
1. Baseline Run: Execute benchmarks with required diagnosers enabled (ensure affected benchmark classes have `[ThreadingDiagnoser]`). Save the HTML/CSV output plus any trace (`.nettrace`) or counters log (
   ```bash
   dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter <FILTERS> -- -f *
   ```
   If additional traces needed for lock wait timing:
   ```bash
   dotnet-trace collect --process-id <PID_FROM_BENCHMARK> --providers System.Runtime:0x4:5 --duration <seconds>
   ```
   Or live counters snapshot (second window = benchmark duration or representative subset):
   ```bash
   dotnet-counters monitor --refresh-interval 1 --counters System.Runtime <PID>
   ```
2. Extract Metrics: Use BenchmarkDotNet output for LockContentionCount & MonitorWaitCount. For EventPipe trace, post-process with PerfView or `traceprocessor` to sum contention wait durations. Document any script used (add under `scripts/` if new).
3. Normalize: When needed divide counts by (TotalOperations / 1000). Document how TotalOperations was determined (e.g., iterations * ops per iteration if custom).
4. After Change: Repeat identical procedure (same filters, diagnosers, duration window).
5. Compare: For each metric apply threshold column. If both time regression (>3% mean) and contention metric regress, treat as blocking unless justified with clear functional gain.

Threshold Logic Details:
- Relative % = (After - Before) / Before * 100.
- A regression is actionable if BOTH the relative % exceeds listed threshold AND any absolute floor condition is met to avoid noise (see table).
- If variance is high (>2x standard deviation overlapping), rerun benchmarks (≥3 runs) and take median of medians before concluding regression.
- Document any accepted regression explicitly in commit body with rationale.

Custom Metrics:
- When adding a new synchronization primitive or strategy (e.g., spin-wait, semaphore, async gate), define a new metric row: name, collection method, normalization, threshold.
- Add any custom EventCounter / `EventSource` you introduce to this table with the exact fully-qualified counter name.
- Provide the capture command(s) and, if scripted, commit the script.

Reviewer Checklist Addendum:
- Verify the commit body includes before/after values for every metric row that changed > ±1% (or any that triggered threshold evaluation).
- If any metric was added, ensure the table here was updated in the same commit.

Log / Output Locations:
- BenchmarkDotNet results: `BenchmarkDotNet.Artifacts/results/*.csv|html|md`.
- Trace files: `BenchmarkDotNet.Artifacts/*.nettrace` (if manually collected, place under same folder and reference filename in commit body).
- Counter snapshots (optional): store textual dumps under `BenchmarkDotNet.Artifacts/counters/` (create if absent).

Failure to include required contention metrics or to update this table when introducing new synchronization behavior is grounds for request-changes or revert on performance PRs.

## 4. Performance Evaluation Notes
- Report absolute time BEFORE / AFTER, delta (ns/us) and percentage.
- Report allocation delta (B) and Gen0/Gen1 collections if meaningful.
- Use same environment (Release, same filters) for A/B.
- Treat small micro-changes (<1%) as noise unless repeated runs show stability.

## 5. Patterns & Guidelines
### Caching Patterns
- SingleFlight*: suppress duplicate concurrent factory executions.
- CoalescingMemoryCache: hot-hit cost should approach raw MemoryCache.
- Avoid extra indirection layers unless they demonstrably improve contention.

### Async Factories
- Provide sync / ValueTask overloads to avoid Task allocation when completion is synchronous.
- Fast-path completed Task / ValueTask (`IsCompletedSuccessfully`).

### Cancellation Semantics
- User cancellation should cancel waiting only unless explicit abort semantics are documented.

### Allocation Minimization
- Use static lambdas / local static functions to avoid captures.
- Avoid boxing; centralize type erasure (one layer only) if needed.

### Error Handling
- Do not cache transient exceptions unless implementing negative caching explicitly.
- Ensure failed factories clear in-flight markers.

## 6. Benchmark Authoring Conventions
- Use `[Params]` for concurrency, key diversity, and workload size where it changes scaling.
- Keep microbenchmark work minimal and deterministic.
- Separate contention scenarios into their own benchmark classes.

## 7. Potential Future Enhancements (Track Separately)
- `CACHE_DIAGNOSTICS` conditional diagnostics.
- Automated regression gate comparing JSON output.
- Negative caching wrapper type.

## 8. PR / Review Checklist
- [ ] Single focused change
- [ ] All tests pass
- [ ] Baseline + after benchmarks included
- [ ] No unintended regressions (hits especially)
- [ ] Allocation impact acceptable / improved
- [ ] Concurrency safety intact
- [ ] API additions documented & justified
- [ ] Commit message includes metrics (perf changes)

## 9. Rollback Policy
If a core scenario regresses (>5% time or notable alloc increase) with no quick mitigation, revert immediately & iterate separately.

## 10. Commit Message Perf Template
```
perf(area): concise summary

Benchmarks (filter: *Example_Filter*)
Scenario              Before        After        Delta      %Change   Alloc Before  Alloc After  Alloc Δ
Foo_Hit              105.0 ns      101.8 ns     -3.2 ns    -3.0%      256 B        248 B        -8 B
Foo_Miss             240.0 ns      239.1 ns     -0.9 ns    -0.4%      264 B        256 B        -8 B

Environment: .NET 9, Windows 11, RyuJIT AVX2
Notes: (any caveats)
```

## 11. Post-Commit Audit
If a perf commit merged without metrics, open a follow-up PR immediately adding the missing before/after data or revert.

---
These instructions are binding for both human contributors and AI assistants to maintain consistent, measurable performance quality.
