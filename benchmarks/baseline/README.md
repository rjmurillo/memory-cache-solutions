
# Benchmark Baselines (Per-OS/Arch Robustness)

This folder stores committed benchmark baseline JSON files used by the BenchGate comparison utility. Each file is specific to an OS and CPU architecture, ensuring robust, matrix-safe gating in CI and local workflows.

## Naming Convention
- `CacheBenchmarks.<os>.<arch>.json` (e.g., `CacheBenchmarks.windows-latest.x64.json`, `CacheBenchmarks.ubuntu-latest.x64.json`, `CacheBenchmarks.macos-latest.x64.json`)
- `ContentionBenchmarks.<os>.<arch>.json` for contention suite baselines

Each matrix job in CI and each local run should use its own OS/arch-specific file. **Never overwrite another platform's baseline.**

## Baseline Update Policy
- Only update a baseline intentionally, after confirming an improvement or accepted tradeoff, and always in a separate commit.
- Never update baselines automatically in CI.
- Never mix baseline file updates with code changes that alter performance.

## Update Procedure (after a verified improvement)
1. Run benchmarks locally with full exporter enabled:
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

## CI and Artifact Forensics
- CI jobs never overwrite committed baselines. Each job uploads both the current and baseline JSON as artifacts for post-mortem analysis.
- Download both the current and baseline JSON artifacts for any failed job to compare and investigate.
- Use the uploaded Markdown and HTML for human-readable reports.

For full details, see `.github/copilot-instructions.md`.
