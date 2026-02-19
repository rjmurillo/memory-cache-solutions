using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;

using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Unit.Shared;

/// <summary>
/// Metric collection harness backed by the OpenTelemetry <see cref="OpenTelemetry.Exporter.InMemoryExporter{T}"/>.
/// Provides detailed validation capabilities for testing accurate metric emission
/// from metered cache implementations using industry-standard tooling.
/// </summary>
/// <remarks>
/// <para>
/// This harness creates an OpenTelemetry <see cref="MeterProvider"/> configured with
/// <see cref="OpenTelemetry.Exporter.InMemoryExporter{T}"/> to capture metrics emitted by cache implementations.
/// It replaces the previous hand-rolled <see cref="MeterListener"/>-based approach with
/// the canonical OTel testing pattern, ensuring metrics are collected and aggregated
/// exactly as they would be in production pipelines.
/// </para>
/// <para>
/// The harness uses cumulative temporality so that each <see cref="Collect"/> call
/// produces a complete snapshot of all aggregated metric points. Filtering by
/// instrument name is performed at query time rather than via Views, keeping setup simple.
/// </para>
/// <para>
/// <b>Thread safety:</b> All public methods serialise through <see cref="_lock"/>.
/// Methods that need a fresh snapshot call <see cref="CollectUnsafe"/> while already
/// holding the lock, avoiding the TOCTOU window that would exist if <see cref="Collect"/>
/// released the lock before the caller re-acquired it.
/// </para>
/// </remarks>
internal sealed class MetricCollectionHarness : IDisposable
{
    private readonly List<Metric> _exportedMetrics = new();
    private readonly MeterProvider _meterProvider;
    private readonly string[] _instrumentNames;

    // Cached snapshot populated on each CollectUnsafe() call.
    // All reads and writes are guarded by _lock.
    private List<MetricMeasurement> _measurements = new();
    private Dictionary<string, List<MetricMeasurement>> _measurementsByInstrument = new();
    private Dictionary<string, long> _aggregatedCounters = new();
    private Dictionary<string, long> _baselineMeasurements = new();
    private readonly object _lock = new();
    private int _disposed; // 0 = active, 1 = disposed; use Volatile/Interlocked for cross-thread visibility

    /// <summary>
    /// Gets all captured metric measurements after taking a fresh snapshot.
    /// </summary>
    /// <value>A read-only list of all metric measurements captured by this harness.</value>
    public IReadOnlyList<MetricMeasurement> AllMeasurements
    {
        get
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                CollectUnsafe();
                return _measurements.ToArray();
            }
        }
    }

    /// <summary>
    /// Gets aggregated counter values by instrument name after taking a fresh snapshot.
    /// </summary>
    /// <value>A read-only dictionary mapping instrument names to their aggregated counter values.</value>
    public IReadOnlyDictionary<string, long> AggregatedCounters
    {
        get
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                CollectUnsafe();
                return new Dictionary<string, long>(_aggregatedCounters);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollectionHarness"/> class.
    /// </summary>
    /// <param name="instrumentNames">The names of the metric instruments to capture.</param>
    public MetricCollectionHarness(params string[] instrumentNames) : this(null, instrumentNames)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricCollectionHarness"/> class with meter filtering.
    /// </summary>
    /// <param name="meterNameFilter">Optional meter name filter to only capture metrics from specific meters.</param>
    /// <param name="instrumentNames">The names of the metric instruments to capture.</param>
    public MetricCollectionHarness(string? meterNameFilter, params string[] instrumentNames)
    {
        _instrumentNames = instrumentNames;

        var builder = Sdk.CreateMeterProviderBuilder();

        if (meterNameFilter != null)
        {
            builder.AddMeter(meterNameFilter);
        }
        else
        {
            // Wildcard – capture all meters
            builder.AddMeter("*");
        }

        builder.AddInMemoryExporter(_exportedMetrics);

        _meterProvider = builder.Build()
            ?? throw new InvalidOperationException(
                "MeterProvider.Build() returned null. " +
                $"Meter filter: '{meterNameFilter ?? "*"}', " +
                $"Instruments: [{string.Join(", ", instrumentNames)}].");
    }

    /// <summary>
    /// Takes a fresh snapshot by flushing the OpenTelemetry pipeline.
    /// </summary>
    /// <remarks>
    /// <see cref="MeterProvider.ForceFlush(int)"/> triggers the metric reader to invoke
    /// Observable instrument callbacks and export the current cumulative values into
    /// <see cref="_exportedMetrics"/>. The exported list is cleared before flushing so
    /// that only the latest snapshot is retained. The entire clear→flush→rebuild
    /// sequence is serialised under <see cref="_lock"/> because <c>ForceFlush</c> is
    /// synchronous and the <c>InMemoryExporter</c> writes to <see cref="_exportedMetrics"/>
    /// on the calling thread. Safe to call multiple times — each call produces an
    /// idempotent snapshot.
    /// </remarks>
    public void Collect()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            CollectUnsafe();
        }
    }

    /// <summary>
    /// Gets all measurements for a specific instrument name after taking a fresh snapshot.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
    /// <returns>A read-only list of measurements for the specified instrument.</returns>
    public IReadOnlyList<MetricMeasurement> GetMeasurements(string instrumentName)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            CollectUnsafe();
            return _measurementsByInstrument.TryGetValue(instrumentName, out var measurements)
                ? measurements.ToArray()
                : Array.Empty<MetricMeasurement>();
        }
    }

    /// <summary>
    /// Gets measurements for a specific instrument that have all the required tags.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
    /// <param name="requiredTags">The tags that must be present in the measurements.</param>
    /// <returns>A read-only list of measurements that match the tag criteria.</returns>
    public IReadOnlyList<MetricMeasurement> GetMeasurementsWithTags(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
    {
        return GetMeasurements(instrumentName)
            .Where(m => requiredTags.All(required =>
                m.Tags.TryGetValue(required.Key, out var tagValue) &&
                Equals(tagValue, required.Value)))
            .ToList();
    }

    /// <summary>
    /// Gets the aggregated count for measurements matching specific tags.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
    /// <param name="requiredTags">The tags that must be present in the measurements.</param>
    /// <returns>The sum of all measurement values that match the tag criteria.</returns>
    public long GetAggregatedCount(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
    {
        return GetMeasurementsWithTags(instrumentName, requiredTags).Sum(m => m.Value);
    }

    /// <summary>
    /// Asserts that the aggregated value for an instrument matches the expected total.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to check.</param>
    /// <param name="expectedCount">The expected aggregated value.</param>
    public void AssertMeasurementCount(string instrumentName, int expectedCount)
    {
        AssertAggregatedCount(instrumentName, expectedCount);
    }

    /// <summary>
    /// Waits asynchronously for a metric to reach the expected value within the specified timeout.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to wait for.</param>
    /// <param name="expectedCount">The expected aggregated value.</param>
    /// <param name="timeout">The maximum time to wait for the metric.</param>
    /// <returns><see langword="true"/> if the metric reached the expected value; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> WaitForMetricAsync(string instrumentName, int expectedCount, TimeSpan timeout)
    {
        ThrowIfDisposed();
        const int delayMs = 10;
        const int maxSafetyIterations = 2000; // Upper bound regardless of timeout
        int maxIterations = Math.Min((int)(timeout.TotalMilliseconds / delayMs) + 1, maxSafetyIterations);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int iteration = 0;

        while (stopwatch.Elapsed < timeout && iteration++ < maxIterations)
        {
            ThrowIfDisposed();
            long currentValue;
            lock (_lock)
            {
                CollectUnsafe();
                currentValue = _aggregatedCounters.GetValueOrDefault(instrumentName, 0);
            }

            if (currentValue >= expectedCount)
            {
                return true;
            }

            await Task.Delay(delayMs, CancellationToken.None).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Waits asynchronously for measurements with specific tags to reach the expected value within the specified timeout.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to wait for.</param>
    /// <param name="expectedCount">The expected aggregated value.</param>
    /// <param name="timeout">The maximum time to wait for the metric.</param>
    /// <param name="requiredTags">The tags that must be present in the measurements.</param>
    /// <returns><see langword="true"/> if the metric reached the expected value; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> WaitForMetricWithTagsAsync(string instrumentName, int expectedCount, TimeSpan timeout, params KeyValuePair<string, object?>[] requiredTags)
    {
        ThrowIfDisposed();
        const int delayMs = 10;
        const int maxSafetyIterations = 2000;
        int maxIterations = Math.Min((int)(timeout.TotalMilliseconds / delayMs) + 1, maxSafetyIterations);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int iteration = 0;

        while (stopwatch.Elapsed < timeout && iteration++ < maxIterations)
        {
            // GetMeasurementsWithTags calls GetMeasurements which calls CollectUnsafe under lock
            var currentValue = GetMeasurementsWithTags(instrumentName, requiredTags).Sum(m => m.Value);
            if (currentValue >= expectedCount)
            {
                return true;
            }

            await Task.Delay(delayMs, CancellationToken.None).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Asserts that the aggregated counter value for an instrument matches the expected total.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to check.</param>
    /// <param name="expectedTotal">The expected aggregated counter value.</param>
    /// <exception cref="Xunit.Sdk.EqualException">Thrown when the actual total doesn't match the expected total.</exception>
    public void AssertAggregatedCount(string instrumentName, long expectedTotal)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            CollectUnsafe();
            Assert.Equal(expectedTotal, _aggregatedCounters.GetValueOrDefault(instrumentName, 0));
        }
    }

    /// <summary>
    /// Asserts that all measurements for an instrument have the required tags with matching values.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to check.</param>
    /// <param name="requiredTags">The tags that must be present in all measurements.</param>
    /// <exception cref="Xunit.Sdk.TrueException">Thrown when a measurement is missing a required tag.</exception>
    /// <exception cref="Xunit.Sdk.EqualException">Thrown when a tag value doesn't match the expected value.</exception>
    public void AssertAllMeasurementsHaveTags(string instrumentName, params KeyValuePair<string, object?>[] requiredTags)
    {
        var measurements = GetMeasurements(instrumentName);
        Assert.NotEmpty(measurements);
        Assert.All(measurements, m =>
        {
            foreach (var requiredTag in requiredTags)
            {
                Assert.True(m.Tags.ContainsKey(requiredTag.Key),
                    $"Measurement missing required tag '{requiredTag.Key}'");
                Assert.Equal(requiredTag.Value, m.Tags[requiredTag.Key]);
            }
        });
    }

    /// <summary>
    /// Resets the harness by clearing all captured measurements and recording the current
    /// cumulative counter values as a baseline. Subsequent counter values returned will be
    /// relative to this baseline.
    /// </summary>
    /// <remarks>
    /// Because the harness uses cumulative temporality, a true reset of the underlying
    /// OpenTelemetry SDK state is not possible without recreating the <see cref="MeterProvider"/>.
    /// This method stores the current cumulative values as a baseline and subtracts them from
    /// future reads, effectively simulating a reset.
    /// </remarks>
    public void Reset()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            CollectUnsafe();

            foreach (var measurement in _measurements)
            {
                var key = GetMeasurementKey(measurement.InstrumentName, measurement.Tags);
                var existingBaseline = _baselineMeasurements.GetValueOrDefault(key, 0);
                _baselineMeasurements[key] = measurement.Value + existingBaseline;
            }

            _measurements.Clear();
            _measurementsByInstrument.Clear();
            _aggregatedCounters.Clear();
            _exportedMetrics.Clear();
        }
    }

    /// <summary>
    /// Disposes the harness and the underlying <see cref="MeterProvider"/>.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _meterProvider.Dispose();
    }

    /// <summary>
    /// Flushes the OTel pipeline and rebuilds the snapshot.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void CollectUnsafe()
    {
        _exportedMetrics.Clear();

        try
        {
            _meterProvider.ForceFlush(10_000); // 10 s safety cap for test environments
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is ObjectDisposedException))
        {
            // Expected when cache is disposed before collection.
        }
        catch (ObjectDisposedException)
        {
            // Expected when cache is disposed before collection.
        }

        RebuildSnapshotUnsafe();
    }

    /// <summary>
    /// Rebuilds the internal measurement/aggregation caches from the exported OTel metrics.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void RebuildSnapshotUnsafe()
    {
        var measurements = new List<MetricMeasurement>();
        var byInstrument = new Dictionary<string, List<MetricMeasurement>>();
        var counters = new Dictionary<string, long>();

        foreach (var metric in _exportedMetrics)
        {
            if (_instrumentNames.Length > 0 && !_instrumentNames.Contains(metric.Name))
            {
                continue;
            }

            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                var tagDict = new Dictionary<string, object?>();
                foreach (var tag in point.Tags)
                {
                    tagDict[tag.Key] = tag.Value;
                }

                long rawValue;
                try
                {
                    rawValue = point.GetSumLong();
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    throw new InvalidOperationException(
                        $"Cannot read sum for instrument '{metric.Name}' (MetricType={metric.MetricType}). " +
                        "MetricCollectionHarness only supports Counter/ObservableCounter instruments with long values.",
                        ex);
                }

                var tagsReadOnly = new ReadOnlyDictionary<string, object?>(tagDict);
                var measurementKey = GetMeasurementKey(metric.Name, tagsReadOnly);
                var adjustedValue = rawValue - _baselineMeasurements.GetValueOrDefault(measurementKey, 0);

                var measurement = new MetricMeasurement(
                    metric.Name,
                    adjustedValue,
                    tagsReadOnly,
                    point.EndTime.UtcDateTime);

                measurements.Add(measurement);

                if (!byInstrument.TryGetValue(metric.Name, out var list))
                {
                    list = new List<MetricMeasurement>();
                    byInstrument[metric.Name] = list;
                }

                list.Add(measurement);
                counters[metric.Name] = counters.GetValueOrDefault(metric.Name, 0) + adjustedValue;
            }
        }

        _measurements = measurements;
        _measurementsByInstrument = byInstrument;
        _aggregatedCounters = counters;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    /// <summary>
    /// Creates a unique key for a measurement based on instrument name and tags.
    /// Used for tracking per-tag-combination baselines.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument.</param>
    /// <param name="tags">The tags associated with the measurement.</param>
    /// <returns>A unique string key combining the instrument name and sorted tags.</returns>
    private static string GetMeasurementKey(string instrumentName, IReadOnlyDictionary<string, object?> tags)
    {
        if (tags.Count == 0)
        {
            return instrumentName;
        }

        var sortedTags = string.Join("|", tags.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{instrumentName}|{sortedTags}";
    }
}

/// <summary>
/// Represents a single metric measurement with all associated metadata.
/// </summary>
internal sealed record MetricMeasurement(
    string InstrumentName,
    long Value,
    IReadOnlyDictionary<string, object?> Tags,
    DateTime Timestamp);
