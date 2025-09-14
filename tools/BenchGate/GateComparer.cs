using BenchGate.Statistics;

namespace BenchGate;

/// <summary>
/// Represents a benchmark sample with statistical measurements.
/// </summary>
/// <param name="Id">The unique identifier for this benchmark sample.</param>
/// <param name="Mean">The mean execution time in nanoseconds.</param>
/// <param name="StdDev">The standard deviation of execution times.</param>
/// <param name="N">The number of measurements taken.</param>
/// <param name="AllocBytes">The total allocated bytes during execution.</param>
/// <param name="Samples">Optional list of individual sample measurements.</param>
public sealed record BenchmarkSample(string Id, double Mean, double StdDev, int N, double AllocBytes, List<double>? Samples = null);

/// <summary>
/// Compares benchmark samples between baseline and current runs to detect performance regressions and improvements.
/// </summary>
/// <param name="timeThresholdPct">The percentage threshold for time-based regressions.</param>
/// <param name="allocThresholdBytes">The absolute threshold in bytes for allocation regressions.</param>
/// <param name="allocThresholdPct">The percentage threshold for allocation regressions.</param>
/// <param name="sigmaMult">The sigma multiplier for statistical significance testing.</param>
/// <param name="useSigma">Whether to use sigma-based statistical significance testing.</param>
public sealed class GateComparer(
    double timeThresholdPct,
    int allocThresholdBytes,
    double allocThresholdPct,
    double sigmaMult,
    bool useSigma)
{
    /// <summary>
    /// Compares baseline and current benchmark samples to identify performance regressions and improvements.
    /// </summary>
    /// <param name="baseline">The baseline benchmark samples to compare against.</param>
    /// <param name="current">The current benchmark samples to evaluate.</param>
    /// <returns>A tuple containing lists of regression and improvement descriptions.</returns>
    public (List<string> regressions, List<string> improvements) Compare(IEnumerable<BenchmarkSample> baseline, IEnumerable<BenchmarkSample> current)
    {
        var baseMap = baseline.ToDictionary(b => b.Id, b => b);
        List<string> regressions = [];
        List<string> improvements = [];

        foreach (var cur in current)
        {
            if (!baseMap.TryGetValue(cur.Id, out var b)) continue; // new -> ignore
            EvaluatePair(b, cur, regressions, improvements);
        }
        return (regressions, improvements);
    }

    private void EvaluatePair(BenchmarkSample baseline, BenchmarkSample current, List<string> regressions, List<string> improvements)
    {
        double meanDelta = current.Mean - baseline.Mean;
        double meanPct = meanDelta / baseline.Mean;
        double allocDelta = current.AllocBytes - baseline.AllocBytes;
        double allocPct = baseline.AllocBytes <= 0 ? 0 : allocDelta / baseline.AllocBytes;

        var regression = false;
        var improvement = false;
        string? statDetail = null;

        // Use internal Mann–Whitney U test if both have samples (two‑sided for symmetry)
        if (baseline.Samples != null && baseline.Samples.Count > 3 && current.Samples != null && current.Samples.Count > 3)
        {
            var baseArr = baseline.Samples.ToArray();
            var curArr = current.Samples.ToArray();
            var test = MannWhitney.Test(baseArr, curArr, Alternative.TwoSided);
            double pValue = test.PValue;
            double medianBase = GetMedian(baseline.Samples);
            double medianCur = GetMedian(current.Samples);
            double medianDelta = medianCur - medianBase;
            double medianPct = medianBase <= 0 ? 0 : medianDelta / medianBase;
            statDetail = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[MWU p={pValue:F4}, median {medianBase:F2}ns -> {medianCur:F2}ns ({medianPct * 100:F2}%)]");
            // Treat as regression if p indicates a significant shift and median increased past thresholds
            regression = pValue < 0.05 && medianPct > timeThresholdPct && medianDelta > 5.0;
            improvement = pValue < 0.05 && medianDelta < 0;
        }
        else
        {
            // Fallback to mean/sigma logic
            bool significant = IsSignificant(baseline, current, meanDelta);
            regression = significant && meanPct > timeThresholdPct && meanDelta > 5.0;
            improvement = meanDelta < 0;
        }

        bool allocRegression = allocDelta > allocThresholdBytes && allocPct > allocThresholdPct;
        bool allocImprovement = allocDelta < 0;

        if (regression || allocRegression)
        {
            regressions.Add(FormatLine(baseline, current, meanPct, allocDelta) + (statDetail != null ? " " + statDetail : ""));
        }
        else if (improvement || allocImprovement)
        {
            improvements.Add(FormatLine(baseline, current, meanPct, allocDelta) + (statDetail != null ? " " + statDetail : ""));
        }
    }

    private static double GetMedian(List<double> values)
    {
        if (values == null || values.Count == 0) return 0;
        var sorted = values.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n % 2 == 1)
            return sorted[n / 2];
        else
            return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private bool IsSignificant(BenchmarkSample baseline, BenchmarkSample current, double meanDelta)
    {
        if (!useSigma) return true;
        double seBase = baseline.N > 0 ? baseline.StdDev / Math.Sqrt(baseline.N) : baseline.StdDev;
        double seCur = current.N > 0 ? current.StdDev / Math.Sqrt(current.N) : current.StdDev;
        double combinedSe = Math.Sqrt(seBase * seBase + seCur * seCur);
        if (combinedSe <= 0) return true; // can't evaluate; treat as significant to avoid masking real regression
        return Math.Abs(meanDelta) > sigmaMult * combinedSe;
    }

    private static string FormatLine(BenchmarkSample baseline, BenchmarkSample current, double meanPct, double allocDelta)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}: mean {1:F2}ns -> {2:F2}ns ({3:F2}%), alloc {4}B -> {5}B (Δ {6}B)",
            current.Id, baseline.Mean, current.Mean, meanPct * 100,
            baseline.AllocBytes, current.AllocBytes, allocDelta);
}
