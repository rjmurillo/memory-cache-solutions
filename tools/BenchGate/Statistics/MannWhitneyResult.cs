namespace BenchGate.Statistics;

/// <summary>
/// Result of a Mann–Whitney U (Wilcoxon rank?sum) test comparing two independent samples.
/// </summary>
/// <param name="U1">The U statistic for the first sample (x) assuming sample ordering as provided.</param>
/// <param name="U2">The U statistic for the second sample (y). Note U2 = n1 * n2 - U1.</param>
/// <param name="U">The smaller of <paramref name="U1"/> and <paramref name="U2"/> (used for two?sided decisions).</param>
/// <param name="Z">Z score from the normal approximation (0 when approximation not used or degenerate).</param>
/// <param name="PValue">Computed p-value for the requested <see cref="Alternative"/> hypothesis.</param>
/// <param name="N1">Size of first sample.</param>
/// <param name="N2">Size of second sample.</param>
/// <param name="UsedNormalApprox">True when the large-sample normal approximation (with tie correction) was applied.</param>
/// <remarks>
/// Interpretation guidelines:
/// <list type="bullet">
/// <item>Smaller <see cref="U"/> (or equivalently, large |<see cref="Z"/>|) suggests stronger evidence of a shift.</item>
/// <item>When all observations are equal (or ties make the variance zero) the test becomes degenerate; in that case <see cref="UsedNormalApprox"/> is false and <see cref="PValue"/> is 1 if both U statistics equal their mean, else 0.</item>
/// <item>The test is non?parametric and assesses stochastic ordering / median shift without assuming normality.</item>
/// </list>
/// Effect size estimation (not implemented here) can use rank-biserial correlation r = 1 - 2U / (n1*n2) where U is the smaller statistic.</remarks>
public readonly record struct MannWhitneyResult(
    double U1, double U2, double U, double Z, double PValue, int N1, int N2, bool UsedNormalApprox);