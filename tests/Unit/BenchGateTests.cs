using System;
using System.Collections.Generic;
using BenchGate;
// reference tools project namespace
using Xunit;

namespace Unit;

public class BenchGateTests
{
    private static BenchmarkSample Sample(string id, double mean, double stdDev = 10, int n = 100, double alloc = 0) => new(id, mean, stdDev, n, alloc);

    [Fact]
    public void NoRegression_WhenMeansWithinThreshold()
    {
        var baseline = new[] { Sample("A", 1000) };
        var current = new[] { Sample("A", 1025) }; // 2.5% < 3%
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);
        Assert.Empty(regressions);
        Assert.Empty(improvements);
    }

    [Fact]
    public void DetectsTimeRegression()
    {
        var baseline = new[] { Sample("A", 1000, stdDev: 5) };
        var current = new[] { Sample("A", 1100, stdDev: 5) }; // 10% > 3%
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, _) = comparer.Compare(baseline, current);
        Assert.Single(regressions);
    }

    [Fact]
    public void AllocationRegressionRequiresBothPercentAndAbsolute()
    {
        var baseline = new[] { Sample("A", 1000, alloc: 100) };
        var current = new[] { Sample("A", 1000, alloc: 105) }; // +5B < 16B absolute guard
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, _) = comparer.Compare(baseline, current);
        Assert.Empty(regressions);

        current = [Sample("A", 1000, alloc: 150)]; // +50B > 16B and 50% > 3%
        (regressions, _) = comparer.Compare(baseline, current);
        Assert.Single(regressions);
    }

    [Fact]
    public void ImprovementReported()
    {
        var baseline = new[] { Sample("A", 1000) };
        var current = new[] { Sample("A", 900) }; // improvement
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);
        Assert.Empty(regressions);
        Assert.Single(improvements);
    }

    [Fact]
    public void SigmaFiltersInsignificantDelta()
    {
        // Means differ by 2% but standard error large enough so not significant
        var baseline = new[] { Sample("A", 1000, stdDev: 200, n: 10) };
        var current = new[] { Sample("A", 1020, stdDev: 200, n: 10) };
        var comparer = new GateComparer(0.03, 16, 0.03, 5.0, useSigma: true); // high sigma multiplier
        var (regressions, improvements) = comparer.Compare(baseline, current);
        Assert.Empty(regressions);
        Assert.Empty(improvements);
    }

    [Fact]
    public void NewBenchmarkIgnored()
    {
        var baseline = new[] { Sample("A", 1000) };
        var current = new[] { Sample("A", 1000), Sample("B", 500) }; // B is new
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);
        Assert.Empty(regressions);
        Assert.Empty(improvements);
    }
}
