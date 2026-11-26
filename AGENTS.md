# AGENTS.md

Read the linked file before you change the matching area.

```text
[Index]|root: .
|.claude/CLAUDE.md: build and test order, BenchGate, per-OS baselines, perf commit evidence
|.claude/rules/testing.md (tests/**): unique Meter and cache names via SharedUtilities, flaky-test patterns
|.claude/rules/csharp.md (*.cs): XML docs on public and internal members, no #region
|tests/Benchmarks.Async/README.md: async hit benchmarks, direct and selfcheck modes, SDK 11 pin
|benchmarks/baseline/README.md: baseline file naming and refresh
```

## Benchmark harness facts

- Count a hit only when `TryGetValue` returns true. A cached null is still a hit.
- Never infer a hit from a non-null value.
- A custom timing harness warms every method before it times any method.
- Tier-0 JIT code skews whichever method runs first.
- Wrap harness setup and timing in `try`/`finally`.
- Dispose every cache and `Meter` in the `finally` block, as `[GlobalCleanup]` does.
- Change one factor per benchmark comparison.
- If two factors stay bundled, say so in the prose.
- Prose claims must match the code and the measured numbers.
- Update the README when the workload changes.
