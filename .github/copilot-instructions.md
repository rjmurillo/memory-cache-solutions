# Copilot / Automation Workflow Guidance

Start every change by opening and reading this file (.github/copilot-instructions.md). Apply it together with .github/instructions/README.md.

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
6. Compare Mean, Allocated, Gen0, contention metrics. If any unrelated core scenario regresses >3% (time) or allocations increase without justification, FIX or ABORT.
7. If perf change: ensure 100% test coverage of new branches/logic (add tests if necessary).
8. Update docs / comments impacted.
9. Craft commit message including before/after table (see template). DO NOT COMMIT perf-affecting change without numbers.
10. If you accidentally committed without numbers, immediately produce an A/B benchmark and either amend commit or add a corrective docs commit with metrics.

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
