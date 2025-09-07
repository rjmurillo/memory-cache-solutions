using System.Text.Json;
using System.Text.Json.Nodes;

namespace BenchGateApp;

internal static class Program
{
    private static int Fail(string msg)
    {
        Console.Error.WriteLine("BENCH GATE FAILURE: " + msg);
        return 1;
    }

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: BenchGate <baseline.json|baselineDir> <current.json> [--suite=<SuiteName>] [--time-threshold=0.03] [--alloc-threshold-bytes=16] [--alloc-threshold-pct=0.03] [--sigma-mult=2.0] [--no-sigma]");
            Console.WriteLine("If a directory is supplied for baseline, BenchGate will attempt per-OS resolution: <suite>.<os>.<arch>.json -> <suite>.<os>.json -> <suite>.json");
            Console.WriteLine("If no baseline file is found after resolution the gate is SKIPPED (exit 0) so initial baselines can be captured.");
            return 2;
        }

        string baselineArg = args[0];
        string currentPath = args[1];

        double timeThreshold = 0.03; // 3%
        int allocThresholdBytes = 16; // 16 bytes absolute guard
        double allocThresholdPct = 0.03; // 3%
        double sigmaMult = 2.0; // default ~95% confidence heuristic
        bool useSigma = true;

        string? suiteName = null;

        foreach (var a in args.Skip(2))
        {
            if (a.StartsWith("--time-threshold=", StringComparison.OrdinalIgnoreCase))
                timeThreshold = double.Parse(a.AsSpan("--time-threshold=".Length));
            else if (a.StartsWith("--alloc-threshold-bytes=", StringComparison.OrdinalIgnoreCase))
                allocThresholdBytes = int.Parse(a.AsSpan("--alloc-threshold-bytes=".Length));
            else if (a.StartsWith("--alloc-threshold-pct=", StringComparison.OrdinalIgnoreCase))
                allocThresholdPct = double.Parse(a.AsSpan("--alloc-threshold-pct=".Length));
            else if (a.StartsWith("--sigma-mult=", StringComparison.OrdinalIgnoreCase))
                sigmaMult = double.Parse(a.AsSpan("--sigma-mult=".Length));
            else if (string.Equals(a, "--no-sigma", StringComparison.OrdinalIgnoreCase))
                useSigma = false;
            else if (a.StartsWith("--suite=", StringComparison.OrdinalIgnoreCase))
                suiteName = a.Substring("--suite=".Length);
        }

        if (!File.Exists(currentPath))
            return Fail($"Current results not found: {currentPath}");

        // Load current first (may need to infer suite name for directory baseline resolution)
        JsonNode currentRoot = JsonNode.Parse(File.ReadAllText(currentPath))!;
        suiteName ??= InferSuiteName(currentRoot) ?? "UnknownSuite";

        string? resolvedBaseline = ResolveBaselinePath(baselineArg, suiteName);
        if (resolvedBaseline is null || !File.Exists(resolvedBaseline))
        {
            Console.WriteLine($"No baseline found for suite '{suiteName}'. Gate SKIPPED. (Expected at: {resolvedBaseline})");
            return 0; // skip so first CI run can establish baseline
        }

        JsonNode baselineRoot = JsonNode.Parse(File.ReadAllText(resolvedBaseline))!;

        var baselineBenchmarks = baselineRoot["Benchmarks"]!.AsArray();
        var currentBenchmarks = currentRoot["Benchmarks"]!.AsArray();

        static BenchmarkSample Map(JsonNode? node)
        {
            if (node is null) return new BenchmarkSample("<null>", 0, 0, 0, 0);
            string id = node["FullName"]!.GetValue<string>();
            var stats = node["Statistics"]!;
            double mean = stats["Mean"]!.GetValue<double>();
            double stdDev = stats["StandardDeviation"]?.GetValue<double>() ?? 0;
            int n = stats["N"]?.GetValue<int>() ?? 0; // BDN may not expose; fallback 0
            double alloc = node["Memory"]?["AllocatedBytes"]?.GetValue<double>() ?? 0;
            return new BenchmarkSample(id, mean, stdDev, n, alloc);
        }

        var baselineSamples = baselineBenchmarks.Select(Map).ToList();
        var currentSamples = currentBenchmarks.Select(Map).ToList();

        var comparer = new GateComparer(timeThreshold, allocThresholdBytes, allocThresholdPct, sigmaMult, useSigma);
        var (regressions, improvements) = comparer.Compare(baselineSamples, currentSamples);

        if (regressions.Count > 0)
        {
            Console.Error.WriteLine("Detected performance regressions vs baseline:");
            foreach (var r in regressions) Console.Error.WriteLine("  " + r);
            return 1;
        }

        Console.WriteLine("Benchmark gate passed (no regressions).");
        if (improvements.Count > 0)
        {
            Console.WriteLine("Notable improvements (consider updating baseline):");
            foreach (var i in improvements) Console.WriteLine("  " + i);
        }
        return 0;
    }

    private static string? ResolveBaselinePath(string baselineArg, string suiteName)
    {
        if (File.Exists(baselineArg)) return baselineArg; // explicit file
        if (!Directory.Exists(baselineArg)) return baselineArg; // non-existent path -> returned path will be tested by caller

        string osId = GetOsId();
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        // Candidate priority
        string[] candidates = new[]
        {
            Path.Combine(baselineArg, $"{suiteName}.{osId}.{arch}.json"),
            Path.Combine(baselineArg, $"{suiteName}.{osId}.json"),
            Path.Combine(baselineArg, $"{suiteName}.json")
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                Console.WriteLine($"Resolved baseline: {c}");
                return c;
            }
        }
        // Return last candidate (expected final path even if missing so caller can display)
        return candidates.LastOrDefault();
    }

    private static string GetOsId()
    {
        if (OperatingSystem.IsWindows()) return "windows-latest"; // Matches CI label
        if (OperatingSystem.IsMacOS()) return "macos-latest";
        if (OperatingSystem.IsLinux())
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            return arch is System.Runtime.InteropServices.Architecture.Arm64 or System.Runtime.InteropServices.Architecture.Arm
                ? "ubuntu-24.04-arm"
                : "ubuntu-latest";
        }
        return "unknown-os";
    }

    private static string? InferSuiteName(JsonNode currentRoot)
    {
        try
        {
            var title = currentRoot["Title"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(title))
            {
                int dot = title.IndexOf('.');
                if (dot >= 0 && dot + 1 < title.Length)
                {
                    string after = title[(dot + 1)..];
                    int dash = after.IndexOf('-');
                    if (dash > 0)
                        return after.Substring(0, dash);
                }
            }
            var firstBench = currentRoot["Benchmarks"]?.AsArray().FirstOrDefault();
            var fullName = firstBench?["FullName"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Split('.');
                if (parts.Length >= 2) return parts[1];
            }
        }
        catch { }
        return null;
    }
}
