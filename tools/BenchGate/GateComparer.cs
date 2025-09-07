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

            double meanDelta = cur.Mean - b.Mean;
            double meanPct = meanDelta / b.Mean;

            bool significant = true;
            if (_useSigma)
            {
                double seBase = b.N > 0 ? b.StdDev / Math.Sqrt(b.N) : b.StdDev;
                double seCur = cur.N > 0 ? cur.StdDev / Math.Sqrt(cur.N) : cur.StdDev;
                double combinedSe = Math.Sqrt(seBase * seBase + seCur * seCur);
                if (combinedSe > 0)
                {
                    significant = Math.Abs(meanDelta) > _sigmaMult * combinedSe;
                }
            }

            double allocDelta = cur.AllocBytes - b.AllocBytes;
            double allocPct = b.AllocBytes <= 0 ? 0 : allocDelta / b.AllocBytes;

            bool timeRegression = significant && meanPct > _timeThresholdPct && meanDelta > 5.0;
            bool allocRegression = allocDelta > _allocThresholdBytes && allocPct > _allocThresholdPct;

            if (timeRegression || allocRegression)
            {
                regressions.Add($"{cur.Id}: mean {b.Mean:F2}ns -> {cur.Mean:F2}ns ({meanPct*100:F2}%), alloc {b.AllocBytes}B -> {cur.AllocBytes}B (Δ {allocDelta}B)");
            }
            else if (meanDelta < 0 || allocDelta < 0)
            {
                improvements.Add($"{cur.Id}: mean {b.Mean:F2}ns -> {cur.Mean:F2}ns ({meanPct*100:F2}%), alloc {b.AllocBytes}B -> {cur.AllocBytes}B (Δ {allocDelta}B)");
            }
        }

        return (regressions, improvements);
    }
}
