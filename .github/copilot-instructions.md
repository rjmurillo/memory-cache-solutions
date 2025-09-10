# Copilot / Automation Workflow Guidance

START OF EVERY SESSION / CHANGE (NON‑NEGOTIABLE):

1. OPEN and READ this file (`.github/copilot-instructions.md`) in full before performing ANY action (search, edit, build, test, benchmark, or commit).
2. The AI assistant must explicitly confirm in its first response after user intent that it has re-read these instructions. If it cannot access the file, it must STOP and request access—never proceed on memory alone.
3. Any action taken without this confirmation is a process violation and subject to immediate correction.

Start every change by opening and reading this file (.github/copilot-instructions.md).

### Baseline Files (Per-OS/Arch Robustness)

Store suite baselines here, using per-OS/arch naming:

```
benchmarks/baseline/CacheBenchmarks.windows-latest.x64.json
benchmarks/baseline/CacheBenchmarks.ubuntu-latest.x64.json
benchmarks/baseline/CacheBenchmarks.macos-latest.x64.json
benchmarks/baseline/ContentionBenchmarks.windows-latest.x64.json (optional)
```

Each matrix job in CI and each local run should use its own OS/arch-specific file. Never overwrite another platform's baseline.

**Baseline update policy:**

- Only update a baseline intentionally, after confirming an improvement or accepted tradeoff, and always in a separate commit.
- Never update baselines automatically in CI.

## 2. Change Categories (Examples)

### CI Integration (Matrix-Safe, Artifact-Rich)

`ci.yml` runs benchmarks on each matrix OS/arch, writes the result to a unique file (e.g., `current.windows-latest.x64.json`), and compares against the matching baseline (e.g., `CacheBenchmarks.windows-latest.x64.json`).

**Artifacts uploaded for every matrix job:**

- The current run's JSON (e.g., `current.windows-latest.x64.json`)
- The resolved baseline JSON (e.g., `CacheBenchmarks.windows-latest.x64.json`)
- Markdown and HTML reports
- Test results and coverage

**No baseline is ever overwritten in CI.**

**Debugging/forensics:**

- You can always download both the current and baseline JSON for any failed job to compare and investigate.

**Example artifact names:**

```
bench-artifacts-windows-latest-x64/
  current.windows-latest.x64.json
  CacheBenchmarks.windows-latest.x64.json
  current-contention.windows-latest.x64.json
  ContentionBenchmarks.windows-latest.x64.json
  *.md
  *.html
  *.trx
  coverage.cobertura.xml
```

| Feature | feat | `feat(coalescing-cache): add TryGetOrCreateAsync with created flag` |
| Refactor (no behavior change) | refactor | `refactor(metered): extract metric emission helper` |
| Tests | test | `test(single-flight): add cancellation scenario` |
| Docs | docs | `docs: add copilot workflow instructions` |
| Benchmark infra | chore | `chore(bench): parametrize contention levels` |

## 3. Standard Implementation Steps

### Local Workflow (Per-OS/Arch)

1. Run benchmarks for your platform:

   ```powershell
   dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *
   # Find the produced JSON (e.g., BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json)
   $os = "windows-latest" # or your platform
   $arch = "x64" # or your arch
   Copy-Item BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json benchmarks/baseline/CacheBenchmarks.$os.$arch.json
   git add benchmarks/baseline/CacheBenchmarks.$os.$arch.json
   git commit -m "chore(bench): update CacheBenchmarks baseline ($os/$arch)" -m "Include before/after metrics table"
   ```

2. To compare without committing:

   ```powershell
   dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *
   Copy-Item BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json BenchmarkDotNet.Artifacts/results/current.$os.$arch.json
   dotnet run -c Release --project tools/BenchGate/BenchGate.csproj -- benchmarks/baseline/CacheBenchmarks.$os.$arch.json BenchmarkDotNet.Artifacts/results/current.$os.$arch.json
   ```

3. Never overwrite another platform's baseline. Always use your OS/arch-specific file.

   ```powershell
   dotnet format
   dotnet build -c Release
   ```

4. Run tests:

   ```powershell
   dotnet test -c Release
   ```

### Updating Baselines (Safe, Per-OS/Arch)

1. Verify improvement locally (no regressions on unrelated scenarios).
2. Commit the code change (perf or feature) with metrics table referencing BEFORE (previous baseline) vs AFTER.
3. After merge (or in a follow-up PR), regenerate benchmarks and update only your platform's baseline in a distinct commit: `chore(bench): update CacheBenchmarks.$os.$arch.json baseline` with summary.
4. Never mix baseline file updates with code changes that alter performance.
5. Never update baselines in CI jobs—always do it intentionally and review the before/after metrics.
6. If perf change: ensure 100% test coverage of new branches/logic (add tests if necessary).
7. Update docs / comments impacted.

### Handling Noisy Micro Benchmarks

If operations are too small (< ~50 ns) and cause frequent false positives:

- Increase work per operation (e.g., a loop inside the benchmark method) or configure BenchmarkDotNet attributes (`[MinIterationTime]`, `[InvocationCount]`).
- Optionally raise the absolute time delta guard (e.g., require >20 ns) by editing BenchGate thresholds or passing a custom `--time-threshold` plus modifying the code for a higher absolute filter.

### Artifact Forensics and Debugging

- Download both the current and baseline JSON artifacts for any failed job to compare and investigate.
- Use the uploaded Markdown and HTML for human-readable reports.
- Never trust a single run—always compare across multiple jobs and platforms if investigating a regression.

### 3.a ABSOLUTE NON-NEGOTIABLE EXECUTION ORDER (AI & Humans)

For EVERY change (docs-only excluded) you MUST execute, in this EXACT order, before proceeding to any subsequent step, review, or commit claim:

0. Ensure local tools are available:

   ```powershell
   dotnet tool restore
   ```

1. Run code formatter:

   ```powershell
   dotnet format
   ```

2. Format Markdown and JSON files (after editing any _.md or _.json files):

   ```powershell
   dotnet tool run pprettier --write .
   ```

3. Build (fail = STOP):

   ```powershell
   dotnet build -c Release
   ```

4. Run tests (fail or new failures = STOP & FIX):

   ```powershell
   dotnet test -c Release
   ```

5. If the change touches ANY implementation that is exercised by a benchmark (directly or indirectly), you MUST:
   a. Capture a BEFORE benchmark (if not already captured in this working session) using the minimal representative filter(s).  
   b. Apply the code change.  
   c. Re-run the SAME benchmark filters for AFTER.  
   d. Compare and record Mean, Alloc (B), Gen0+ collections, and contention metrics (if applicable).  
   e. Only proceed if thresholds hold or improvements are justified.

AI Assistant Compliance Clause: The assistant SHALL NOT skip or merely describe these steps; it MUST execute them with tooling available. If tooling is unavailable, it must declare itself BLOCKED and await remediation—not proceed on assumption.

### 3.b PowerShell Guarded Command Pattern (Standardized Invocation)

All automation MUST use a guarded invocation pattern to guarantee synchronous completion, surface success/failure explicitly, and produce a unique, parse-friendly marker. This prevents race conditions or partial output assumptions.

Preferred pattern template (one command at a time):

```powershell
try { <COMMAND> } finally { if ($?) { echo "CMD_<GUID>=0" } else { echo "CMD_<GUID>=$LASTEXITCODE" } }
```

Example (build):

```powershell
try { dotnet build -c Release } finally { if ($?) { echo "BUILD_38e3f3e930144ed7b95b0e608218d0fe=0" } else { echo "BUILD_38e3f3e930144ed7b95b0e608218d0fe=$LASTEXITCODE" } }
```

Minimal variant currently in use (echoes PowerShell success boolean):

```powershell
try { dotnet build -c Release } finally { if ($?) { echo "38e3f3e930144ed7b95b0e608218d0fe=$?" } else { echo "38e3f3e930144ed7b95b0e608218d0fe=$?" } }
```

Guidelines:

- Use a new GUID (or unique token) per command so logs can be machine-parsed.
- Prefer reporting numeric exit codes (0 / non-zero) via `$LASTEXITCODE` for reliability versus `$?` when disambiguating failure types.
- NEVER chain multiple critical commands inside one `try {}` block—each must have its own guard.
- If output truncates or marker line missing, re-run the command; do not proceed.
- Benchmarks should add a BENCH marker, e.g. `BENCH_<GUID>=0`.

Rationale:

- Ensures deterministic detection of completion and exit status across AI and CI contexts.
- Disambiguates partial vs full execution when tools buffer output.
- Provides a stable parsing anchor for future automated gate scripts.

Violation: Executing dotnet/benchmark commands without this pattern is a process breach and subject to correction or revert.

Command Execution Discipline: ALWAYS wait for each PowerShell command (format, build, test, benchmark, gate) to fully complete and capture its exit code/output before issuing the next command. Do NOT pipeline or overlap steps. Use only PowerShell invocation syntax regardless of host OS in this repository context.

Reliable Waiting (PowerShell Core):

1. Invoke each command on its own line (no background `Start-Job`, no trailing `&`).
2. Immediately echo and record an identifiable marker line using the guarded pattern.
3. Treat any non‑zero exit code as a hard STOP—investigate and resolve before continuing.
4. Never chain multiple critical dotnet operations with `;` unless you still emit and validate the prior marker before executing the next.
5. Benchmarks and gate runs must also surface exit code using the pattern.
6. If output appears truncated or missing expected footer lines, re-run the command; do not proceed on partial output.
7. The AI assistant MUST show the captured marker line in conversation before proceeding.

Evidence in Commit / PR: Any perf-impacting commit MUST contain an explicit snippet referencing the BEFORE and AFTER benchmark command(s) and results table (see Section 10 template) plus a PASS indication of format/build/test.

Violation Handling: Any merge or PR lacking this ordered evidence for perf-touching code is subject to immediate reversion.

### Contention Metrics Definition

"Contention Metrics" are a fixed, explicitly collected set of concurrency and stall indicators you MUST baseline and re-measure for any change that can alter synchronization, scheduling, or allocation pressure. They are normalized per 1,000 logical benchmark operations (unless already emitted as a cumulative count for the full run). Add new metrics (with name, source, collection command) to the table below whenever a benchmark depends on them.

| Metric                        | What It Represents                                                | Source / How Collected                                                                                                                                                    | Unit & Normalization          | Threshold Trigger (regression)                                                   |
| ----------------------------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------- | -------------------------------------------------------------------------------- |
| LockContentionCount           | # of monitor/lock acquisitions that experienced contention        | BenchmarkDotNet ThreadingDiagnoser (`[ThreadingDiagnoser]`)                                                                                                               | Raw count per 1,000 ops       | > +5 absolute per 1k ops AND > +3% relative vs baseline                          |
| MonitorWaitCount              | # times threads waited (Monitor.Enter blocking)                   | ThreadingDiagnoser                                                                                                                                                        | Raw count per 1,000 ops       | > +3% relative AND delta ≥ 2 per 1k ops                                          |
| AvgLockWaitTimeNs             | Average time a contended lock waited before acquisition           | Derived: (TotalLockWaitTime / LockContentionCount); collect via EventPipe: `dotnet-trace collect --providers System.Runtime:0x4:5` then aggregate events `ContentionStop` | Nanoseconds (ns)              | > +3% relative OR > +50 ns absolute                                              |
| ThreadPoolQueueLength (peak)  | Maximum queued ThreadPool work items (backpressure indicator)     | `dotnet-counters monitor --refresh-interval 1 --counters System.Runtime` (counter: `threadpool-queue-length`)                                                             | Peak value over benchmark run | > +10% relative and absolute increase ≥ 2                                        |
| ThreadPoolCompletedItemsDelta | Change in completed items per second (throughput proxy)           | `dotnet-counters` (`threadpool-completed-items-count`) sample start/end and divide by elapsed seconds                                                                     | Items/sec                     | > -3% (drop) when time also regresses OR > -5% alone                             |
| GCGen0/1/2 Counts             | # of GCs per generation                                           | BDN default columns / `dotnet-counters` (`gen-0-gc-count`, etc.)                                                                                                          | Count per full benchmark run  | Any increase causing time or contention regression; > +5% counts triggers review |
| GCPauseTimeTotalMs            | Sum of GC pause durations                                         | `dotnet-trace` / PerfView (events: `GCStop`, `GCSuspendEE`, `GCRestartEE`) aggregated                                                                                     | Milliseconds per full run     | > +3% relative AND > 0.5 ms absolute                                             |
| AllocationRateBps (optional)  | Allocation bytes per second (indirect contention via GC pressure) | `dotnet-counters` (`alloc-rate`)                                                                                                                                          | Bytes/sec                     | > +3% with no functional justification                                           |

Collection Procedure:

1. Baseline Run: Execute benchmarks with required diagnosers enabled (ensure affected benchmark classes have `[ThreadingDiagnoser]`). Save the HTML/CSV output plus any trace (`.nettrace`) or counters log:

   ```powershell
   dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter <FILTERS>
   ```

   If additional traces needed for lock wait timing:

   ```powershell
   dotnet-trace collect --process-id <PID_FROM_BENCHMARK> --providers System.Runtime:0x4:5 --duration <seconds>
   ```

   Or live counters snapshot (second window = benchmark duration or representative subset):

   ```powershell
   dotnet-counters monitor --refresh-interval 1 --counters System.Runtime <PID>
   ```

2. Extract Metrics: Use BenchmarkDotNet output for LockContentionCount & MonitorWaitCount. For EventPipe trace, post-process with PerfView or `traceprocessor` to sum contention wait durations. Document any script used (add under `scripts/` if new).
3. Normalize: When needed divide counts by (TotalOperations / 1000). Document how TotalOperations was determined (e.g., iterations \* ops per iteration if custom).
4. After Change: Repeat identical procedure (same filters, diagnosers, duration window).
5. Compare: For each metric apply threshold column. If both time regression (>3% mean) and contention metric regress, treat as blocking unless justified with clear functional gain.

Threshold Logic Details:

- Relative % = (After - Before) / Before \* 100.
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

- SingleFlight\*: suppress duplicate concurrent factory executions.
- CoalescingMemoryCache: hot-hit cost should approach raw MemoryCache.
- Avoid extra indirection layers unless they demonstrably improve contention.

### Async Factories

- Provide sync / ValueTask overloads to avoid Task allocation when completion is synchronous.
- Fast-path completed Task / ValueTask (`IsCompletedSuccessfully`).

### Cancellation Semantics

- User cancellation should cancel waiting only (the caller stops awaiting) while the in-flight factory/background work continues to completion and its result is cached. Example: caller `A` starts a factory, caller `B` starts and cancels its token mid-flight—`B` gets a canceled task, the factory still runs and populates the cache for later hits. Abort scenario (only when explicitly documented): a cancellation token intended to terminate work (e.g., a user-supplied abort token passed directly into the factory) causes both the awaiter and the underlying operation to stop and clears any in-flight marker. Implement full abort semantics ONLY when: (a) the API surface accepts a token whose contract promises cooperative cancellation of the underlying computation, AND (b) partial results would be invalid or harmful to cache.

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

```text
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

## 12. Benchmark Regression Gate (BenchGate)

To automatically detect performance regressions, a lightweight gating tool (`tools/BenchGate`) compares the most recent BenchmarkDotNet JSON output against committed baselines under `benchmarks/baseline/`.

### Outputs Enabled

The benchmark harness (`tests/Benchmarks/Program.cs`) is configured with `GateConfig` to always emit:

- GitHub Markdown (`*-report-github.md`)
- HTML (`*-report.html`)
- Full JSON (`*-report-full.json`) <-- consumed by the gate

### Baseline Files

Store suite baselines here:

```
benchmarks/baseline/CacheBenchmarks.json
benchmarks/baseline/ContentionBenchmarks.json (optional)
```

You may add per-OS variants if matrix differences are material (e.g., `CacheBenchmarks.windows-latest.json`). The CI currently looks for the unified `CacheBenchmarks.json`.

### Threshold Logic

For each benchmark (matched by `FullName`):

- Time regression FAIL when: `currentMean > baselineMean * 1.03` AND absolute delta > 5 ns.
- Allocation regression FAIL when: `currentAllocated > baselineAllocated` AND (delta > 16 B OR >3%).

Both guards reduce noise of tiny fluctuations. Adjust via CLI switches:

```
--time-threshold=0.03           # relative (3%)
--alloc-threshold-bytes=16       # absolute allocation guard
```

### Local Workflow

```
dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *SingleFlight*
Copy BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json benchmarks/baseline/CacheBenchmarks.json
git add benchmarks/baseline/CacheBenchmarks.json
git commit -m "chore(bench): update CacheBenchmarks baseline" -m "Include before/after metrics table"
```

Compare without committing baseline first:

```
dotnet run -c Release --project tests/Benchmarks/Benchmarks.csproj --filter *
Copy BenchmarkDotNet.Artifacts/results/Benchmarks.CacheBenchmarks-report-full.json BenchmarkDotNet.Artifacts/results/current.json
dotnet run -c Release --project tools/BenchGate/BenchGate.csproj -- benchmarks/baseline/CacheBenchmarks.json BenchmarkDotNet.Artifacts/results/current.json
```

### CI Integration

`ci.yml` runs benchmarks on each matrix OS, copies the latest full JSON to a stable `current.json`, and (if a baseline file exists) executes BenchGate. Failures surface in the job logs and fail the build.

Artifacts uploaded:

- `*.json` (including `*-report-full.json` and `current.json`)
- `*.md`, `*.html`

### Updating Baselines

1. Verify improvement locally (no regressions on unrelated scenarios).
2. Commit the code change (perf or feature) with metrics table referencing BEFORE (previous baseline) vs AFTER.
3. After merge (or in a follow-up PR), regenerate benchmarks and update the baseline in a distinct commit: `chore(bench): update CacheBenchmarks baseline` with summary.
4. Never mix baseline file updates with code changes that alter performance.

### Handling Noisy Micro Benchmarks

If operations are too small (< ~50 ns) and cause frequent false positives:

- Increase work per operation (e.g., a loop inside the benchmark method) or configure BenchmarkDotNet attributes (`[MinIterationTime]`, `[InvocationCount]`).
- Optionally raise the absolute time delta guard (e.g., require >20 ns) by editing BenchGate thresholds or passing a custom `--time-threshold` plus modifying the code for a higher absolute filter.

### Adding a New Suite

1. Create new benchmark class.
2. Run once to produce full JSON.
3. Add `<SuiteName>.json` under `benchmarks/baseline/`.
4. (Optional) Extend CI to enforce that suite (add additional compare step).

### Non-Matching Benchmarks

Benchmarks present in current run but absent in baseline are ignored (treated as new). After validating them, refresh baseline intentionally.

### Improvements Reporting

BenchGate lists improvements (time or allocation reductions) so you can decide when to refresh baselines—do not auto-update; keep human review.

---

## 13. Automation & BenchGate Validation Guardrails

To prevent unverified performance infrastructure changes, any modification (code or docs) involving the benchmark harness, BenchGate, thresholds, statistical logic, exporter configuration, CI gating steps, or baseline format MUST follow this explicit validation checklist. AI assistants and human contributors alike are bound by this section. Skipping steps or claiming completion without evidence is a process violation and grounds for rework or revert.

### 13.1 Mandatory Validation Checklist (ALL must be done BEFORE claiming the change is “done”)

0. Ensure local tools are available:

   ```powershell
   dotnet tool restore
   ```

1. Format: `dotnet format` succeeds. Note that code may be modified to fit style and analyzer rules.
2. Format Markdown and JSON files (after editing any _.md or _.json files):

   ```powershell
   dotnet tool run pprettier --write .
   ```

3. Build: `dotnet build -c Release` succeeds (no warnings newly introduced for changed files unless justified and documented).
4. Tests: `dotnet test -c Release` green (add/adjust tests if logic changed).
5. Produce Fresh Benchmark Output:
   - Run at least ONE representative suite (e.g., `--filter *SingleFlight*` OR full `*`) generating a new `*-report-full.json`.
6. Local Gate PASS Scenario:
   - Copy that JSON to a temp baseline path and execute BenchGate against itself (should PASS with zero regressions reported).
7. Local Gate FAIL Simulation (Proof of Detection):
   - Create a TEMPORARY mutated copy of the current JSON (e.g., increase one Mean by ≥10% or Allocated by +64 B) and re-run BenchGate pointing baseline→original, current→mutated.
   - Confirm BenchGate exits with non‑zero code and lists the synthetic regression.
8. Statistical Branches (when sigma / standard error logic touched):
   - Also create a mutation BELOW thresholds (e.g., +0.5% mean, +2 ns) and verify it does NOT fail (ensures noise filtering still works).
9. Capture Evidence:
   - Include (in commit body or PR comment) a short table:
     - PASS run: exit code, number of benchmarks compared, regressions=0.
     - FAIL run: exit code, name(s) of intentionally mutated benchmark(s), reported deltas.
   - Summarize any new CLI switches & default values.
10. CI Adaptation (if workflow changed):

- Show the diff of `ci.yml` & describe how per-OS / per-suite resolution happens.

11. Baseline Handling:

- NEVER auto-overwrite committed baselines inside the same perf-affecting commit.
- If format changes, provide a migration note + one dedicated commit updating baselines (no code in that commit).

12. Analyzer / Complexity Warnings:

- If new complexity or analyzer warnings arise in touched files, address or justify them explicitly.

### 13.2 Required Evidence Template (paste into PR comment or commit body)

```
BenchGate Validation
Build: PASS
Tests: PASS (X new tests added / updated)
Benchmark Command: <exact command>
PASS Run: exit=0, benchmarks compared=N, regressions=0
FAIL Simulation: exit=1, mutated=<BenchmarkFullName>, ΔMean=+X ns (+Y%), (or) ΔAlloc=+Z B
Noise Guard Check (small delta): PASS (no false positive)
New/Changed Flags: --sigma-mult=2.0 (default), added --no-sigma (disables significance filtering)
Notes: <any deviations>
```

### 13.3 AI Assistant Specific Rules

- MUST execute (not just describe) build + test + at least two BenchGate runs (pass + fail) using available tooling before asserting completion.
- MUST NOT mark a task “complete” in conversation or PR guidance until evidence template lines are populated.
- MUST refuse to finalize if tool execution results are missing or inconsistent with claims.
- MUST explicitly call out if workspace lacks a current `*-report-full.json` and generate one instead of assuming.

### 13.4 Common Pitfalls (Do NOT do these)

- Declaring statistical logic “done” without verifying a regression is actually caught.
- Relying solely on code inspection for threshold correctness.
- Mutating production baseline JSON just to force a regression (instead, copy & mutate a temp current file).
- Bundling baseline file updates with logic changes.

### 13.5 Rapid Sanity Script (Optional)

If repeated frequently, consider adding a helper script under `scripts/benchgate-validate.ps1` encapsulating steps 3–5; still, evidence output must be included manually.

---

## 14. Incremental Pattern-Oriented Development Hierarchy

A "Maslow's Hierarchy" for changes: progress ONLY when lower layers are satisfied. Every commit should move cleanly at (or up) one layer without skipping evidence at lower layers.

### 14.1 Layers (Bottom → Top)

1. Testability & Fast Feedback
   - Write or extend unit/benchmark tests FIRST that would fail without the intended change (red → green → refactor).
   - Prefer deterministic, allocation-stable tests; isolate flakiness early.
2. Structural Qualities
   - Loose coupling: depend on abstractions/interfaces; no hidden singletons.
   - High cohesion: each class has one focused responsibility (SRP). If a method > ~40 LOC with branching, consider extraction BEFORE adding logic.
   - Explicit boundaries: internal vs public APIs; internal helpers sealed/internal.
3. Safety & Evidence
   - Tests green locally (unit + any new micro-bench harness assertions).
   - BenchGate PASS (plus synthetic FAIL simulation if infra touched).
   - Static analysis / complexity thresholds satisfied (justify any suppression inline).
4. Patterns & Practices
   - Apply only when justified by concrete forces (e.g., Strategy for pluggable eviction, Decorator for metrics layering). Avoid speculative abstraction.
   - Prefer composition over inheritance; if inheritance used, assert invariants via tests.
5. Optimization & Micro-Perf
   - Optimize only AFTER a repeatable benchmark proves a hotspot (≥5% potential gain) and tests lock correctness.
   - Show before/after benchmark table in commit body; if <1% improvement, aggregate with other micro-ops or skip.
6. GoF / Advanced Architectural Moves
   - Introduce higher-level patterns (e.g., Flyweight, Observer) only when they reduce measured contention or memory footprint with a net positive complexity tradeoff.

### 14.2 Required Commit Structure Per Layer

| Layer | Mandatory Artifacts                                                                                                  |
| ----- | -------------------------------------------------------------------------------------------------------------------- |
| 1     | Failing test added (red) → implementation (green); commit includes passing state only (squash intermediate locally). |
| 2     | Refactor commit with NO behavior change (tests unchanged & green) + note of structural improvement.                  |
| 3     | Evidence block: test summary + BenchGate PASS output snippet.                                                        |
| 4     | Rationale: problem forces → chosen pattern → alternative rejected.                                                   |
| 5     | Benchmark table (before/after) + explanation of root cause & technique.                                              |
| 6     | Risk assessment (complexity delta, extension points) + migration notes if public API surface shifts.                 |

### 14.3 Enforcement Rules

- A performance commit without a preceding (or combined) test demonstrating correctness is invalid.
- Introducing a pattern without a concrete force (contention, duplication, instability) is grounds for revert.
- BenchGate evidence (Section 13) is REQUIRED for any layer ≥3 that can affect perf or infra.
- Failure to provide before/after metrics when claiming optimization triggers automatic follow-up or revert.

### 14.4 PR Review Checklist Addendum

Add to existing Section 8 checklist:

- [ ] Layer(s) targeted: \_\_\_\_ (1–6). Lower layers satisfied.
- [ ] New/changed tests cover behavior & edge conditions.
- [ ] BenchGate evidence included (if layer ≥3).
- [ ] Structural changes reduce complexity or improve clarity (not lateral churn).
- [ ] Pattern introduction justified by explicit forces.
- [ ] Micro-optimizations backed by ≥3% stable improvement or aggregated.

### 14.5 Fast Test Design Guidelines

- Prefer pure logic extraction into small internal static methods for direct unit testing.
- For concurrency primitives: add deterministic tests using controlled Task schedulers or manual reset events to avoid timing races.
- Guard against allocation regressions by asserting `GC.GetAllocatedBytesForCurrentThread()` deltas inside micro-tests where feasible (tolerances ±16 B).

### 14.6 Refactor Workflow (Safe Extraction)

1. Identify high-complexity method (analyzer warning).
2. Add characterization tests if coverage sparse.
3. Extract cohesive helpers (private static or internal sealed types) ensuring no hidden mutable shared state.
4. Re-run tests + BenchGate (sanity) if extraction touches hot path.
5. Only then proceed to new feature or optimization atop the cleaner structure.

### 14.7 Anti-Patterns to Reject

- Premature interface proliferation without multiple concrete strategies.
- Benchmark-driven changes committed without archived result JSON.
- Pattern layering increasing indirection on hot paths with <1% gain.
- Catch-all utility classes housing unrelated helpers.

### 14.8 Minimal Example (Commit Body Snippet)

```
refactor(single-flight): extract result awaiter path

Layer: 2 (Structural Qualities)
Rationale: Reduce cognitive complexity (26→12) in ExecuteAsync by isolating fast-path.
Tests: Existing SingleFlight* tests all green (no new behaviors).
BenchGate: PASS (0 regressions, compared=14)
```

---

## When reviewing C# code (\*.cs)

I need your help tracking down and fixing some bugs that have been reported in this codebase.

I suspect the bugs are related to:

- Incorrect handling of edge cases
- Off-by-one errors in loops or array indexing
- Unexpected data types
- Uncaught exceptions
- Concurrency issues
- Improper configuration settings

To diagnose:

1. Review the code carefully and systematically
2. Trace the relevant code paths
3. Consider boundary conditions and potential error states
4. Look for antipatterns that tend to cause bugs
5. Run the code mentally with example inputs
6. Think about interactions between components

When you find potential bugs, for each one provide:

1. File path and line number(s)
2. Description of the issue and why it's a bug
3. Example input that would trigger the bug
4. Suggestions for how to fix it

After analysis, please update the code with your proposed fixes. Try to match the existing code style. Add regression tests if possible to prevent the bugs from recurring.

I appreciate your diligence and attention to detail! Let me know if you need any clarification on the intended behavior of the code.

---

## XML Documentation Comments Guidance for C#

When writing XML documentation comments for C# code, follow these best practices to ensure clarity, consistency, and compatibility with documentation tools:

### General Principles
- **Document all public types and members** with XML comments. Private/internal members may be documented if useful.
- **Always use complete sentences** ending with periods.
- **XML must be well-formed**; invalid XML will cause compiler warnings.
- **At a minimum, every type and member should have a `<summary>` tag.**

### Tag Usage and Structure
- Use `<summary>` to briefly describe the purpose of a type or member.
- Use `<remarks>` for additional or supplemental information.
- Use `<param name="paramName">` to describe each method parameter. The name must match the method signature.
- Use `<returns>` to describe the return value of a method.
- Use `<exception cref="ExceptionType">` to document exceptions that may be thrown.
- Use `<value>` to describe the value of a property.
- Use `<typeparam name="T">` and `<typeparamref name="T"/>` for generic type parameters.
- Use `<example>` to provide usage examples, often with `<code>` blocks.
- Use `<para>`, `<list>`, `<c>`, `<code>`, `<b>`, `<i>`, `<u>`, `<br/>`, and `<a>` for formatting and structure as needed.
- Use `<inheritdoc/>` to inherit documentation from base members or interfaces when appropriate.

### Referencing Types, Members, and Keywords
- **Whenever referencing a type, member, or exception in documentation, you MUST use `<see cref="..."/>` or `<seealso cref="..."/>`.**
   - Example: `See <see cref="System.String"/> for details.`
   - Example: `Throws <see cref="ArgumentNullException"/> if <paramref name="foo"/> is null.`
- **When referencing C# language keywords (such as `true`, `false`, `null`, `async`, etc.), use `<see langword="keyword"/>`.**
   - Example: `Returns <see langword="true"/> if the operation succeeds; otherwise, <see langword="false"/>.`
- **Do NOT use plain text for code elements or keywords.** Always use the appropriate XML tag for references.
- For inline code or identifiers that are not references, use `<c>code</c>`.

### Additional Best Practices
- Use `<paramref name="paramName"/>` to refer to parameters in documentation text.
- Use `<typeparamref name="T"/>` to refer to generic type parameters in text.
- For external URLs, use `<see href="https://example.com">Link Text</see>` or `<seealso href="https://example.com">Link Text</seealso>`.
- If you need to show angle brackets in text, use `&lt;` and `&gt;`.
- Avoid duplicating documentation by using `<inheritdoc/>` or `<include/>` where possible.

### Example

```csharp
/// <summary>
/// Attempts to parse the specified <paramref name="input"/> as an integer.
/// Returns <see langword="true"/> if parsing succeeds; otherwise, <see langword="false"/>.
/// </summary>
/// <param name="input">The string to parse.</param>
/// <param name="result">When this method returns, contains the parsed value if successful; otherwise, <see langword="null"/>.</param>
/// <returns><see langword="true"/> if parsing was successful; otherwise, <see langword="false"/>.</returns>
/// <exception cref="ArgumentNullException">Thrown if <paramref name="input"/> is <see langword="null"/>.</exception>
```

For more details, see the [Microsoft Learn XML documentation tags guide](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags).