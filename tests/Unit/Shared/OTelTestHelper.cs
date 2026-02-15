using System.Diagnostics.Metrics;
using CacheImplementations;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Unit.Shared;

/// <summary>
/// Lightweight OpenTelemetry test helpers for unit tests.
/// Creates a MeterProvider with InMemoryExporter for metric assertions.
/// </summary>
internal static class OTelTestHelper
{
    /// <summary>
    /// Creates a MeterProvider that exports to the provided list.
    /// Caller must dispose the returned MeterProvider.
    /// </summary>
    public static MeterProvider CreateMeterProvider(List<Metric> exportedItems, string meterName)
    {
        return Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddInMemoryExporter(exportedItems)
            .Build();
    }

    /// <summary>
    /// Creates a MeterProvider configured for MeteredMemoryCache's meter.
    /// </summary>
    public static MeterProvider CreateCacheMeterProvider(List<Metric> exportedItems)
    {
        return CreateMeterProvider(exportedItems, MeteredMemoryCache.MeterName);
    }

    /// <summary>
    /// Finds a metric by name in the exported items.
    /// </summary>
    public static Metric? FindMetric(List<Metric> metrics, string name)
    {
        return metrics.FirstOrDefault(m => m.Name == name);
    }

    /// <summary>
    /// Gets the sum value of a metric filtered by a specific tag.
    /// </summary>
    public static long GetMetricValueByTag(Metric metric, string tagKey, string tagValue)
    {
        foreach (ref readonly var metricPoint in metric.GetMetricPoints())
        {
            foreach (var tag in metricPoint.Tags)
            {
                if (tag.Key == tagKey && tag.Value?.ToString() == tagValue)
                {
                    return metricPoint.GetSumLong();
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Gets the total sum across all metric points.
    /// </summary>
    public static long GetMetricValue(Metric metric)
    {
        long total = 0;
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            total += mp.GetSumLong();
        }

        return total;
    }

    /// <summary>
    /// Asserts that a metric has the expected value for a specific tag.
    /// </summary>
    public static void AssertMetricValueByTag(Metric metric, string tagKey, string tagValue, long expectedValue)
    {
        var actual = GetMetricValueByTag(metric, tagKey, tagValue);
        Assert.Equal(expectedValue, actual);
    }

    /// <summary>
    /// Asserts that ALL metric points have the specified tag with the expected value.
    /// </summary>
    public static void AssertAllPointsHaveTag(Metric metric, string tagKey, string expectedValue)
    {
        var points = new List<(bool hasTag, string tags)>();
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            var hasTag = false;
            var tagParts = new List<string>();
            foreach (var t in mp.Tags)
            {
                tagParts.Add($"{t.Key}={t.Value}");
            }

            var tagStr = string.Join(", ", tagParts);
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == tagKey && tag.Value?.ToString() == expectedValue)
                {
                    hasTag = true;
                    break;
                }
            }

            points.Add((hasTag, tagStr));
        }

        Assert.All(points, p =>
            Assert.True(p.hasTag, $"MetricPoint missing tag '{tagKey}={expectedValue}'. Tags: [{p.tags}]"));
    }

    /// <summary>
    /// Checks if a metric has a specific tag on any metric point.
    /// </summary>
    public static bool HasTag(Metric metric, string tagKey, string expectedValue)
    {
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == tagKey && tag.Value?.ToString() == expectedValue)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if any metric point has a tag with the given key (regardless of value).
    /// </summary>
    public static bool HasTagKey(Metric metric, string tagKey)
    {
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == tagKey)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all distinct tag values for a given key across all metric points.
    /// </summary>
    public static List<string?> GetTagValues(Metric metric, string tagKey)
    {
        var values = new List<string?>();
        foreach (ref readonly var mp in metric.GetMetricPoints())
        {
            foreach (var tag in mp.Tags)
            {
                if (tag.Key == tagKey)
                {
                    values.Add(tag.Value?.ToString());
                }
            }
        }

        return values;
    }
}
