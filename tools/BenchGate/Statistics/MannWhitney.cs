namespace BenchGate.Statistics;

/// <summary>
/// Provides a minimal implementation of the Mann–Whitney U (Wilcoxon rank‑sum) test for two
/// independent samples with support for a normal approximation (including tie correction).
/// </summary>
/// <remarks>
/// Characteristics:
/// <list type="bullet">
/// <item><b>Input:</b> Two independent samples supplied as <see cref="ReadOnlySpan{Double}"/> to avoid allocations.</item>
/// <item><b>Ties:</b> Supported via mid‑rank assignment; variance is adjusted by the standard tie correction factor.</item>
/// <item><b>Approximation:</b> For all non‑degenerate cases a normal approximation with continuity correction is used.</item>
/// <item><b>Degenerate:</b> If all observations are identical (variance == 0) the p‑value is 1 when both U statistics equal the mean, else 0.</item>
/// <item><b>Alternative hypotheses:</b> Controlled via <see cref="Alternative"/> (two‑sided, greater, less).</item>
/// </list>
/// Complexity: O((n1 + n2) log(n1 + n2)) time due to sorting, O(n1 + n2) additional memory.
/// </remarks>
public static class MannWhitney
{
    /// <summary>
    /// Performs a Mann–Whitney U (Wilcoxon rank‑sum) test comparing two independent samples.
    /// </summary>
    /// <param name="x">First sample (treated as the sample referenced by the <see cref="Alternative"/> hypothesis).</param>
    /// <param name="y">Second sample.</param>
    /// <param name="alt">Alternative hypothesis (default two‑sided).</param>
    /// <returns>A <see cref="MannWhitneyResult"/> containing U statistics, z score (if applicable) and p‑value.</returns>
    /// <exception cref="ArgumentException">Thrown when either sample is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either sample exceeds the defensive size limit.</exception>
    /// <remarks>
    /// The method assigns ranks with mid‑rank handling for ties, accumulates the rank sum for the first sample,
    /// derives U statistics, and (unless the variance collapses) applies a normal approximation with continuity correction (+/‑ 0.5).
    /// For one‑sided alternatives:
    /// <list type="bullet">
    /// <item><see cref="Alternative.Greater"/> tests H1: distribution(x) is stochastically greater than distribution(y).</item>
    /// <item><see cref="Alternative.Less"/> tests H1: distribution(x) is stochastically less than distribution(y).</item>
    /// </list>
    /// Returned p‑values are clamped to [0,1] to guard against numerical noise for extreme z scores.
    /// </remarks>
    public static MannWhitneyResult Test(ReadOnlySpan<double> x, ReadOnlySpan<double> y, Alternative alt = Alternative.TwoSided)
    {
        if (x.Length == 0 || y.Length == 0) throw new ArgumentException("Samples must be non-empty.");
        if (x.Length > 10_000_000) throw new ArgumentOutOfRangeException(nameof(x), "Sample too large.");
        if (y.Length > 10_000_000) throw new ArgumentOutOfRangeException(nameof(y), "Sample too large.");

        int n1 = x.Length, n2 = y.Length, n = n1 + n2;
        if ((long)n > 10_000_000)
            throw new ArgumentOutOfRangeException(nameof(x), "Total sample size too large.");
        var combined = new (double v, int g)[n]; // g: 0 for x, 1 for y
        for (int i = 0; i < n1; i++)
        {
            if (!double.IsFinite(x[i])) throw new ArgumentException("x contains non-finite values.", nameof(x));
            combined[i] = (x[i], 0);
        }
        for (int i = 0; i < n2; i++)
        {
            if (!double.IsFinite(y[i])) throw new ArgumentException("y contains non-finite values.", nameof(y));
            combined[n1 + i] = (y[i], 1);
        }

        Array.Sort(combined, static (a, b) => a.v.CompareTo(b.v));

        double r1 = 0.0;          // Sum of ranks for sample x
        double tieSum = 0.0;      // Sum over ties of (t^3 - t) for tie correction factor
        int iIndex = 0;
#pragma warning disable S1244 // Exact equality is intentional for grouping identical floating point values (ties)
        while (iIndex < n)
        {
            int j = iIndex + 1;
            while (j < n && combined[j].v == combined[iIndex].v) j++;
            double rankAvg = 0.5 * (iIndex + 1 + j); // average rank over [iIndex, j)
            int tieLen = j - iIndex;
            if (tieLen > 1)
            {
                double t = tieLen;
                tieSum += t * t * t - t;
            }
            for (int k = iIndex; k < j; k++) if (combined[k].g == 0) r1 += rankAvg;
            iIndex = j;
        }
#pragma warning restore S1244

        double u1 = r1 - n1 * (n1 + 1) / 2.0;
        double u2 = n1 * (double)n2 - u1;
        double u = Math.Min(u1, u2);

        // Normal approximation with tie correction
        double mu = n1 * (n2) / 2.0;
        double tieCorr = (n > 1) ? tieSum / (12.0 * n * (n - 1)) : 0.0;
        double sigma2 = n1 * (double)n2 * (n + 1) / 12.0 - n1 * (double)n2 * tieCorr;
        if (sigma2 <= 0) // all values equal or extreme ties
        {
#pragma warning disable S1244 // Exact equality check for degenerate distribution detection
            double pDegenerate = (u1 == mu && u2 == mu) ? 1.0 : 0.0;
#pragma warning restore S1244
            return new(u1, u2, u, 0.0, pDegenerate, n1, n2, UsedNormalApprox: false);
        }
        double sigma = Math.Sqrt(sigma2);

        double z = alt switch
        {
            Alternative.TwoSided => (Math.Abs(u - mu) - 0.5) / sigma,
            Alternative.Greater => ((u1 - mu) - 0.5) / sigma,
            Alternative.Less => ((u1 - mu) + 0.5) / sigma,
            _ => throw new ArgumentOutOfRangeException(nameof(alt))
        };

        double zAbs = Math.Abs(z);
        double p = alt switch
        {
            Alternative.TwoSided => TwoSidedP(zAbs),
            Alternative.Greater => UpperTailP(z),
            Alternative.Less => LowerTailP(z),
            _ => double.NaN
        };

        // Clamp numeric noise
        p = Math.Clamp(p, 0.0, 1.0);
        return new(u1, u2, u, z, p, n1, n2, UsedNormalApprox: true);
    }

    private const double Sqrt2 = 1.4142135623730951;

    // Stable p-values via complementary error function
    private static double TwoSidedP(double zAbs) => Erfc(zAbs / Sqrt2);
    private static double UpperTailP(double z) => 0.5 * Erfc(z / Sqrt2);
    private static double LowerTailP(double z) => 0.5 * Erfc(-z / Sqrt2);

    // Stable complementary error function (Hastings, as in Numerical Recipes).
    // Max abs error ~1e-7 for erf; tails are very stable for |x| up to ~10+.
    private static double Erfc(double x)
    {
        double ax = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * ax);

        // tau = t * exp(-x^2 - 1.26551223 + t*(1.00002368 + t*(0.37409196 + t*(0.09678418
        //      + t*(-0.18628806 + t*(0.27886807 + t*(-1.13520398 + t*(1.48851587
        //      + t*(-0.82215223 + t*0.17087277)))))))))
        double poly =
            1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 +
                                                                  t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 +
                                                                      t * 0.17087277)))))));

        double tau = t * Math.Exp(-ax * ax - 1.26551223 + t * poly);
        return x >= 0 ? tau : 2.0 - tau;
    }
}
