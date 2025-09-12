using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BenchGate;
using Xunit;

namespace Unit;

/// <summary>
/// Comprehensive validation tests for BenchGate performance regression detection.
/// Tests both PASS and FAIL scenarios to ensure BenchGate correctly identifies performance regressions
/// and prevents false positives/negatives in CI pipeline validation.
/// </summary>
public class BenchGateValidationTests
{
    private static BenchmarkSample Sample(string id, double mean, double stdDev = 10, int n = 100, double alloc = 0, List<double>? samples = null)
        => new(id, mean, stdDev, n, alloc, samples);

    /// <summary>
    /// Tests that BenchGate PASSES when MeteredMemoryCache performance is within acceptable bounds.
    /// This simulates the happy path where cache performance remains stable.
    /// </summary>
    [Fact]
    public void BenchGate_ShouldPass_WhenMeteredCachePerformanceStable()
    {
        // Arrange: Baseline performance for MeteredMemoryCache operations
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.0, stdDev: 5.0, n: 1000, alloc: 0),
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Miss", mean: 200.0, stdDev: 8.0, n: 1000, alloc: 0),
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.0, stdDev: 12.0, n: 1000, alloc: 24),
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Hit", mean: 155.0, stdDev: 6.0, n: 1000, alloc: 0),
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Miss", mean: 205.0, stdDev: 9.0, n: 1000, alloc: 0)
        };

        // Current performance with minimal differences (well under 3% threshold and absolute 5ns guard)
        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.2, stdDev: 5.0, n: 1000, alloc: 0),      // +0.13%
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Miss", mean: 200.3, stdDev: 8.0, n: 1000, alloc: 0),     // +0.15%
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.5, stdDev: 12.0, n: 1000, alloc: 24),       // +0.14%
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Hit", mean: 155.1, stdDev: 6.0, n: 1000, alloc: 0),       // +0.06%
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Miss", mean: 205.2, stdDev: 9.0, n: 1000, alloc: 0)       // +0.10%
        };

        // Act
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Assert: No regressions detected, performance is stable
        Assert.Empty(regressions);
        Assert.Empty(improvements); // Changes too small to be considered improvements
    }

    /// <summary>
    /// Tests that BenchGate FAILS when MeteredMemoryCache shows significant performance regression.
    /// This validates that BenchGate will catch performance issues before they reach production.
    /// </summary>
    [Fact]
    public void BenchGate_ShouldFail_WhenMeteredCachePerformanceDegrades()
    {
        // Arrange: Baseline performance
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.0, stdDev: 5.0, n: 1000, alloc: 0),
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.0, stdDev: 10.0, n: 1000, alloc: 24)
        };

        // Current performance showing significant regression (>5% degradation)
        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 158.0, stdDev: 5.2, n: 1000, alloc: 0),     // +5.3% regression
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 368.0, stdDev: 10.5, n: 1000, alloc: 24)       // +5.1% regression
        };

        // Act
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Assert: Regressions detected, BenchGate should fail
        Assert.Equal(2, regressions.Count);
        Assert.Contains(regressions, r => r.Contains("TryGetValue_Hit"));
        Assert.Contains(regressions, r => r.Contains("Set_NewEntry"));
        Assert.Empty(improvements);
    }

    /// <summary>
    /// Tests BenchGate's ability to detect memory allocation regressions in MeteredMemoryCache.
    /// Validates both absolute byte threshold (16 bytes) and percentage threshold (3%).
    /// </summary>
    [Fact]
    public void BenchGate_ShouldDetectAllocationRegressions_InMeteredCache()
    {
        // Arrange: Baseline with minimal allocations
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.0, stdDev: 5.0, n: 1000, alloc: 0),        // No allocations
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.0, stdDev: 10.0, n: 1000, alloc: 24),        // 24 bytes baseline
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Set", mean: 360.0, stdDev: 11.0, n: 1000, alloc: 200)      // 200 bytes baseline
        };

        // Current showing allocation regressions
        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.0, stdDev: 5.0, n: 1000, alloc: 8),       // +8B < 16B threshold = OK
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.0, stdDev: 10.0, n: 1000, alloc: 48),        // +24B > 16B and +100% > 3% = FAIL
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Set", mean: 360.0, stdDev: 11.0, n: 1000, alloc: 250)      // +50B > 16B and +25% > 3% = FAIL
        };

        // Act
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Assert: Two allocation regressions detected (TryGetValue_Hit should pass due to low absolute increase)
        Assert.Equal(2, regressions.Count);
        Assert.Contains(regressions, r => r.Contains("Set_NewEntry"));
        Assert.Contains(regressions, r => r.Contains("NamedCache_Set"));

        // TryGetValue_Hit should not regress since 8B < 16B threshold
        Assert.DoesNotContain(regressions, r => r.Contains("TryGetValue_Hit"));
    }

    /// <summary>
    /// Tests BenchGate's statistical significance detection using sigma filtering.
    /// Validates that small performance changes with high variance are not flagged as regressions.
    /// </summary>
    [Fact]
    public void BenchGate_ShouldFilterInsignificantChanges_UsingStatisticalAnalysis()
    {
        // Arrange: High variance baseline (realistic for cache operations under load)
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.ConcurrentAccess", mean: 200.0, stdDev: 40.0, n: 50, alloc: 0),      // High variance
            Sample("Benchmarks.MeteredMemoryCache.StableOperation", mean: 150.0, stdDev: 2.0, n: 1000, alloc: 0)       // Low variance
        };

        // Current with modest changes
        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.ConcurrentAccess", mean: 208.0, stdDev: 42.0, n: 50, alloc: 0),      // +4% but high variance
            Sample("Benchmarks.MeteredMemoryCache.StableOperation", mean: 156.0, stdDev: 2.1, n: 1000, alloc: 0)       // +4% with low variance
        };

        // Act: Use sigma filtering with 2.0 multiplier (~95% confidence)
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: true);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Assert: Only statistically significant regression should be detected
        Assert.Single(regressions);
        Assert.Contains(regressions, r => r.Contains("StableOperation")); // Low variance = significant change

        // High variance operation should not be flagged due to statistical insignificance
        Assert.DoesNotContain(regressions, r => r.Contains("ConcurrentAccess"));
    }

    /// <summary>
    /// Tests BenchGate's handling of new benchmarks that don't exist in baseline.
    /// New benchmarks should be ignored (not cause failures) to support incremental development.
    /// </summary>
    [Fact]
    public void BenchGate_ShouldIgnoreNewBenchmarks_WhenAddingNewTests()
    {
        // Arrange: Original baseline with fewer benchmarks
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.0, stdDev: 5.0, n: 1000, alloc: 0),
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.0, stdDev: 10.0, n: 1000, alloc: 24)
        };

        // Current with additional new benchmarks (simulating feature additions)
        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 151.0, stdDev: 5.1, n: 1000, alloc: 0),       // Existing: +0.7%
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 352.0, stdDev: 10.2, n: 1000, alloc: 24),       // Existing: +0.6%
            Sample("Benchmarks.MeteredMemoryCache.NewFeature_TaggedCache", mean: 160.0, stdDev: 6.0, n: 1000, alloc: 32), // NEW
            Sample("Benchmarks.MeteredMemoryCache.NewFeature_MultiCache", mean: 180.0, stdDev: 8.0, n: 1000, alloc: 16)   // NEW
        };

        // Act
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Assert: No regressions due to new benchmarks, existing benchmarks stable
        Assert.Empty(regressions);
        Assert.Empty(improvements);
    }

    /// <summary>
    /// Tests BenchGate's detection of performance improvements.
    /// Validates that significant improvements are reported for baseline updates.
    /// </summary>
    [Fact]
    public void BenchGate_ShouldDetectImprovements_WhenPerformanceOptimized()
    {
        // Arrange: Original baseline
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 150.0, stdDev: 5.0, n: 1000, alloc: 0),
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 350.0, stdDev: 10.0, n: 1000, alloc: 48),
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Operation", mean: 200.0, stdDev: 8.0, n: 1000, alloc: 32)
        };

        // Current showing significant improvements (>5% better)
        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.TryGetValue_Hit", mean: 140.0, stdDev: 4.8, n: 1000, alloc: 0),       // -6.7% time improvement
            Sample("Benchmarks.MeteredMemoryCache.Set_NewEntry", mean: 330.0, stdDev: 9.5, n: 1000, alloc: 24),        // -5.7% time, -50% allocation
            Sample("Benchmarks.MeteredMemoryCache.NamedCache_Operation", mean: 198.0, stdDev: 7.9, n: 1000, alloc: 32)  // -1% (not significant)
        };

        // Act
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Assert: Significant improvements detected, no regressions
        Assert.Empty(regressions);
        Assert.True(improvements.Count >= 2); // Allow for additional improvements to be detected
        Assert.Contains(improvements, i => i.Contains("TryGetValue_Hit"));
        Assert.Contains(improvements, i => i.Contains("Set_NewEntry"));
    }

    /// <summary>
    /// Tests BenchGate's edge case handling for extreme performance scenarios.
    /// Validates behavior with zero times, very large times, and extreme allocations.
    /// </summary>
    [Fact]
    public void BenchGate_ShouldHandleEdgeCases_GracefullyWithoutExceptions()
    {
        // Arrange: Edge case scenarios
        var baseline = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.ZeroTime", mean: 0.1, stdDev: 0.01, n: 1000, alloc: 0),              // Very fast operation
            Sample("Benchmarks.MeteredMemoryCache.LargeTime", mean: 50000.0, stdDev: 1000.0, n: 100, alloc: 0),        // Slow operation
            Sample("Benchmarks.MeteredMemoryCache.ZeroAlloc", mean: 100.0, stdDev: 5.0, n: 1000, alloc: 0),            // No allocations
            Sample("Benchmarks.MeteredMemoryCache.LargeAlloc", mean: 200.0, stdDev: 10.0, n: 1000, alloc: 100000)      // Large allocations
        };

        var current = new[]
        {
            Sample("Benchmarks.MeteredMemoryCache.ZeroTime", mean: 0.11, stdDev: 0.011, n: 1000, alloc: 0),            // +10%
            Sample("Benchmarks.MeteredMemoryCache.LargeTime", mean: 52000.0, stdDev: 1050.0, n: 100, alloc: 0),        // +4%
            Sample("Benchmarks.MeteredMemoryCache.ZeroAlloc", mean: 104.0, stdDev: 5.2, n: 1000, alloc: 8),            // +4% time, +8B alloc
            Sample("Benchmarks.MeteredMemoryCache.LargeAlloc", mean: 206.0, stdDev: 10.3, n: 1000, alloc: 105000)      // +3% time, +5% alloc
        };

        // Act & Assert: Should not throw exceptions
        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baseline, current);

        // Verify that regressions are detected for significant changes
        Assert.NotEmpty(regressions); // Should detect some regressions

        // Verify that the method handles edge cases gracefully (no exceptions thrown)
        // The exact regressions detected may vary based on BenchGate's internal logic
    }

    /// <summary>
    /// Integration test that simulates the full BenchGate CLI workflow.
    /// Tests JSON parsing, baseline resolution, and complete validation pipeline.
    /// </summary>
    [Fact]
    public void BenchGate_IntegrationTest_ShouldValidateCompleteWorkflow()
    {
        // Arrange: Create synthetic benchmark JSON data
        var baselineJson = CreateBenchmarkJson("MeteredCacheBenchmarks", new[]
        {
            ("Benchmarks.MeteredMemoryCache.Hit_UnnamedCache", 145.2, 4.8, 1000, 0.0),
            ("Benchmarks.MeteredMemoryCache.Miss_UnnamedCache", 195.7, 7.2, 1000, 0.0),
            ("Benchmarks.MeteredMemoryCache.Set_UnnamedCache", 340.1, 11.5, 1000, 24.0),
            ("Benchmarks.MeteredMemoryCache.Hit_NamedCache", 150.3, 5.1, 1000, 0.0),
            ("Benchmarks.MeteredMemoryCache.Miss_NamedCache", 200.8, 8.0, 1000, 0.0),
            ("Benchmarks.MeteredMemoryCache.Set_NamedCache", 355.4, 12.8, 1000, 32.0)
        });

        var currentJson = CreateBenchmarkJson("MeteredCacheBenchmarks", new[]
        {
            ("Benchmarks.MeteredMemoryCache.Hit_UnnamedCache", 147.1, 4.9, 1000, 0.0),    // +1.3% (OK)
            ("Benchmarks.MeteredMemoryCache.Miss_UnnamedCache", 198.2, 7.4, 1000, 0.0),   // +1.3% (OK)
            ("Benchmarks.MeteredMemoryCache.Set_UnnamedCache", 351.7, 11.8, 1000, 24.0),   // +3.4% (FAIL)
            ("Benchmarks.MeteredMemoryCache.Hit_NamedCache", 152.0, 5.2, 1000, 0.0),       // +1.1% (OK)
            ("Benchmarks.MeteredMemoryCache.Miss_NamedCache", 203.5, 8.2, 1000, 0.0),      // +1.3% (OK)
            ("Benchmarks.MeteredMemoryCache.Set_NamedCache", 368.9, 13.1, 1000, 40.0)      // +3.8% time, +25% alloc (FAIL)
        });

        // Act: Parse and compare (simulating BenchGate CLI behavior)
        var baselineRoot = JsonDocument.Parse(baselineJson).RootElement;
        var currentRoot = JsonDocument.Parse(currentJson).RootElement;

        var baselineBenchmarks = ParseBenchmarks(baselineRoot);
        var currentBenchmarks = ParseBenchmarks(currentRoot);

        var comparer = new GateComparer(0.03, 16, 0.03, 2.0, useSigma: false);
        var (regressions, improvements) = comparer.Compare(baselineBenchmarks, currentBenchmarks);

        // Assert: Expected regressions detected
        Assert.Equal(2, regressions.Count);
        Assert.Contains(regressions, r => r.Contains("Set_UnnamedCache"));
        Assert.Contains(regressions, r => r.Contains("Set_NamedCache"));
        Assert.Empty(improvements);
    }

    // Helper Methods

    private static string CreateBenchmarkJson(string title, (string name, double mean, double stdDev, int n, double alloc)[] benchmarks)
    {
        var benchmarkObjects = benchmarks.Select(b => new
        {
            FullName = b.name,
            Statistics = new
            {
                Mean = b.mean,
                StandardDeviation = b.stdDev,
                N = b.n
            },
            Memory = new
            {
                BytesAllocatedPerOperation = b.alloc
            }
        });

        var result = new
        {
            Title = title,
            Benchmarks = benchmarkObjects
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<BenchmarkSample> ParseBenchmarks(JsonElement root)
    {
        var benchmarks = new List<BenchmarkSample>();

        foreach (var benchmark in root.GetProperty("Benchmarks").EnumerateArray())
        {
            var name = benchmark.GetProperty("FullName").GetString()!;
            var stats = benchmark.GetProperty("Statistics");
            var mean = stats.GetProperty("Mean").GetDouble();
            var stdDev = stats.GetProperty("StandardDeviation").GetDouble();
            var n = stats.GetProperty("N").GetInt32();
            var alloc = benchmark.GetProperty("Memory").GetProperty("BytesAllocatedPerOperation").GetDouble();

            benchmarks.Add(new BenchmarkSample(name, mean, stdDev, n, alloc));
        }

        return benchmarks;
    }
}
