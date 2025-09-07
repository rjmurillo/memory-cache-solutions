namespace BenchGateApp;

public sealed record BenchmarkSample(string Id, double Mean, double StdDev, int N, double AllocBytes);

public sealed class GateComparer
{
    private readonly double _timeThresholdPct;
    private readonly int _allocThresholdBytes;
    private readonly double _allocThresholdPct;
    private readonly double _sigmaMult;
    private readonly bool _useSigma;

    public GateComparer(double timeThresholdPct, int allocThresholdBytes, double allocThresholdPct, double sigmaMult, bool useSigma)
    {
        _timeThresholdPct = timeThresholdPct;
        _allocThresholdBytes = allocThresholdBytes;
        _allocThresholdPct = allocThresholdPct;
        _sigmaMult = sigmaMult;
        _useSigma = useSigma;
    }

    public (List<string> regressions, List<string> improvements) Compare(IEnumerable<BenchmarkSample> baseline, IEnumerable<BenchmarkSample> current)
    {
        var baseMap = baseline.ToDictionary(b => b.Id, b => b);
        List<string> regressions = new();
        List<string> improvements = new();

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
        bool significant = IsSignificant(baseline, current, meanDelta);

        double allocDelta = current.AllocBytes - baseline.AllocBytes;
        double allocPct = baseline.AllocBytes <= 0 ? 0 : allocDelta / baseline.AllocBytes;

        bool timeRegression = significant && meanPct > _timeThresholdPct && meanDelta > 5.0;
        bool allocRegression = allocDelta > _allocThresholdBytes && allocPct > _allocThresholdPct;

        if (timeRegression || allocRegression)
        {
            regressions.Add(FormatLine(baseline, current, meanPct, allocDelta));
        }
        else if (meanDelta < 0 || allocDelta < 0)
        {
            improvements.Add(FormatLine(baseline, current, meanPct, allocDelta));
        }
    }

    private bool IsSignificant(BenchmarkSample baseline, BenchmarkSample current, double meanDelta)
    {
        if (!_useSigma) return true;
        double seBase = baseline.N > 0 ? baseline.StdDev / Math.Sqrt(baseline.N) : baseline.StdDev;
        double seCur = current.N > 0 ? current.StdDev / Math.Sqrt(current.N) : current.StdDev;
        double combinedSe = Math.Sqrt(seBase * seBase + seCur * seCur);
        if (combinedSe <= 0) return true; // can't evaluate; treat as significant to avoid masking real regression
        return Math.Abs(meanDelta) > _sigmaMult * combinedSe;
    }

    private static string FormatLine(BenchmarkSample baseline, BenchmarkSample current, double meanPct, double allocDelta)
        => $"{current.Id}: mean {baseline.Mean:F2}ns -> {current.Mean:F2}ns ({meanPct * 100:F2}%), alloc {baseline.AllocBytes}B -> {current.AllocBytes}B (Δ {allocDelta}B)";
}
