# Copilot / Automation Workflow Guidance

This repository uses an automated, performance?focused workflow for cache primitives. Follow these steps when proposing or implementing changes (manually or via Copilot / other AI assistants):

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
1. Benchmark (baseline) the specific target(s) you expect to impact.
   - If unsure which, run ALL benchmarks:
     ```bash
     dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *
     ```
   - To target one family (examples):
     ```bash
     # SingleFlightLazy only
     dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *SingleFlightLazy*
     # Coalescing contention
     dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *Coalescing_Contention*
     ```
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
5. Re?run the focused benchmarks (or all if uncertain).
6. Compare Mean, Allocated, Gen0 (and contention metrics if relevant). Ensure NO regressions, unless intentionally trading one metric for a bigger win (document this in commit body).
7. Commit with a clear Conventional Commit message (see section 2). Include a brief summary of the benchmark delta in the commit body when it's a perf change.

## 4. Performance Evaluation Notes
- Prefer reporting absolute Mean delta + percentage and allocation delta.
- Treat regressions > ~3% as meaningful unless noise is demonstrated.
- For concurrency benchmarks always inspect allocation growth at higher concurrency settings.
- If a change regresses critical hot paths (hits) while improving rare miss paths, reject unless justified.

## 5. Patterns & Guidelines
### Caching Patterns
- SingleFlight* types: goal is to suppress duplicate concurrent factory executions.
- CoalescingMemoryCache: minimize per-call allocations; hot hit should be near raw MemoryCache.
- Avoid extra layers (e.g., unnecessary Lazy indirection) unless they measurably reduce contention.

### Async Factories
- Provide sync / ValueTask overloads where factories often complete synchronously to avoid Task allocation/state machine.
- Fast-path completed Task / ValueTask using `IsCompletedSuccessfully`.

### Cancellation Semantics
- Expose tokens that cancel waiting (observation) but never abandon an already running underlying computation unless explicitly designed.

### Allocation Minimization
- Use static lambdas / local static functions to avoid closure captures when possible.
- Avoid boxing (store concrete generic instances where feasible or a single erasure layer only once in a dictionary).

### Error Handling
- Do not cache transient exceptions by default unless implementing negative caching intentionally.
- Ensure failed factories remove in-flight markers to permit retries.

## 6. Benchmark Authoring Conventions
- Add [Params] for concurrency or key diversity when contention characteristics matter.
- Isolate contention scenarios in dedicated benchmark classes (e.g., ContentionBenchmarks) so micro benchmarks stay single-threaded.
- Keep factory work minimal and deterministic for hot-path microbenchmarks.

## 7. Potential Future Enhancements (Track Separately)
- Optional compilation symbol (e.g., `CACHE_DIAGNOSTICS`) to include diagnostic tags/counters only when enabled.
- Benchmark regression gating (compare JSON output of last known good run).
- Negative caching result wrapper to prevent stampedes on repeated failures.

## 8. PR / Review Checklist
- [ ] Single focused change
- [ ] Tests pass
- [ ] Benchmarks before/after captured (attach summary or gist)
- [ ] No perf regressions on unrelated scenarios
- [ ] Allocations not increased (or justified)
- [ ] Concurrency safety preserved
- [ ] API surface additions justified & documented

## 9. Rollback Policy
If a perf change regresses a core scenario (>5% slowdown or significant allocation increase) and no quick fix is available, revert promptly; re?submit once addressed.

## 10. Example Commit Body (Perf)
```
perf(coalescing-cache): reduce miss allocation by avoiding double Lazy wrapping

Benchmarks (Concurrency=16):
Before: Miss 2,180 ns / 3,272 B
After : Miss 1,950 ns / 2,640 B  (?10.6% time, ?19.3% alloc)
No change in hit path (1,460 ns ±2%).
```

---
These instructions are intended for both human contributors and AI assistants to maintain consistent, measurable performance quality across cache components.
