using System.Diagnostics.Metrics;

namespace Unit.Shared;

/// <summary>
/// Enhanced metric collection harness that provides detailed validation capabilities
/// for testing accurate metric emission from metered cache implementations.
/// </summary>
/// <remarks>
/// <para>
/// This harness captures OpenTelemetry metrics emitted by cache implementations and provides
/// comprehensive validation utilities. It's thread-safe and designed to work with the
/// .NET metrics system to capture counter measurements with their associated tags.
/// </para>
/// <para>
/// The harness automatically aggregates counter values and provides filtering capabilities
/// by instrument name and meter name, making it easy to validate specific metric emissions
/// in complex test scenarios.
/// </para>
/// </remarks>
internal sealed class MetricCollectionHarness : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<MetricMeasurement> _measurements = new();
    private readonly Dictionary<string, List<MetricMeasurement>> _measurementsByInstrument = new();
    private readonly Dictionary<string, long> _aggregatedCounters = new();
    private readonly string[] _instrumentNames;
    private readonly string? _meterNameFilter;
    private readonly object _lock = new object();

    /// <summary>
    /// Gets all captured metric measurements after taking a fresh snapshot.
    /// </summary>
    /// <value>A read-only list of all metric measurements captured by this harness.</value>
    public IReadOnlyList<MetricMeasurement> AllMeasurements
    {
        get
        {
            Collect();
            lock (_lock)
            {
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
            Collect();
            lock (_lock)
            {
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
        _meterNameFilter = meterNameFilter;

        _listener.InstrumentPublished = (inst, listener) =>
        {
            if (instrumentNames.Contains(inst.Name) &&
                (_meterNameFilter == null || inst.Meter.Name == _meterNameFilter))
            {
                listener.EnableMeasurementEvents(inst);
            }
        };

        _listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            var tagDict = tags.ToArray().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var metricMeasurement = new MetricMeasurement(inst.Name, measurement, tagDict, DateTime.UtcNow);

            lock (_lock)
            {
                _measurements.Add(metricMeasurement);

                if (!_measurementsByInstrument.TryGetValue(inst.Name, out var instrumentMeasurements))
                {
                    instrumentMeasurements = new List<MetricMeasurement>();
                    _measurementsByInstrument[inst.Name] = instrumentMeasurements;
                }

                instrumentMeasurements.Add(metricMeasurement);

                _aggregatedCounters[inst.Name] = _aggregatedCounters.GetValueOrDefault(inst.Name, 0) + measurement;
            }
        });
        _listener.Start();
    }

    /// <summary>
    /// Takes a fresh snapshot of all Observable instrument values.
    /// Clears previous measurements and records current absolute values.
    /// </summary>
    /// <remarks>
    /// Observable instruments (ObservableCounter, ObservableUpDownCounter, ObservableGauge) report
    /// absolute accumulated values, not deltas. This method clears accumulated data first, then
    /// records fresh values so that aggregation (ADD) produces correct totals.
    /// Safe to call multiple times — each call produces an idempotent snapshot.
    /// </remarks>
    public void Collect()
    {
        lock (_lock)
        {
            _measurements.Clear();
            _measurementsByInstrument.Clear();
            _aggregatedCounters.Clear();
            _listener.RecordObservableInstruments();
        }
    }

    /// <summary>
    /// Gets all measurements for a specific instrument name after taking a fresh snapshot.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to get measurements for.</param>
    /// <returns>A read-only list of measurements for the specified instrument.</returns>
    public IReadOnlyList<MetricMeasurement> GetMeasurements(string instrumentName)
    {
        Collect();
        lock (_lock)
        {
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
        // GetMeasurements already calls Collect()
        return GetMeasurements(instrumentName)
            .Where(m => requiredTags.All(required =>
                m.Tags.ContainsKey(required.Key) &&
                Equals(m.Tags[required.Key], required.Value)))
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
        // GetMeasurementsWithTags already calls Collect() via GetMeasurements
        return GetMeasurementsWithTags(instrumentName, requiredTags).Sum(m => m.Value);
    }

    /// <summary>
    /// Asserts that the aggregated value for an instrument matches the expected total.
    /// </summary>
    /// <param name="instrumentName">The name of the instrument to check.</param>
    /// <param name="expectedCount">The expected aggregated value.</param>
    /// <remarks>
    /// With Observable instruments, each instrument reports one measurement per tag set
    /// per collection. This asserts the total value, not the number of callback invocations.
    /// </remarks>
    public void AssertMeasurementCount(string instrumentName, int expectedCount)
    {
        // With Observable instruments, measurement count = number of unique tag sets,
        // not number of operations. Assert on the aggregated value instead.
        Collect();
        lock (_lock)
        {
            Assert.Equal(expectedCount, _aggregatedCounters.GetValueOrDefault(instrumentName, 0));
        }
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            Collect();
            long currentValue;
            lock (_lock)
            {
                currentValue = _aggregatedCounters.GetValueOrDefault(instrumentName, 0);
            }
            if (currentValue >= expectedCount)
            {
                return true;
            }
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var currentValue = GetMeasurementsWithTags(instrumentName, requiredTags).Sum(m => m.Value);
            if (currentValue >= expectedCount)
            {
                return true;
            }
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
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
        Collect();
        lock (_lock)
        {
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
        // GetMeasurements already calls Collect()
        var measurements = GetMeasurements(instrumentName);
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
    /// Resets the harness by clearing all captured measurements and aggregated counters.
    /// </summary>
    /// <remarks>
    /// This method is useful for reusing a harness across multiple test scenarios
    /// within the same test method.
    /// </remarks>
    public void Reset()
    {
        lock (_lock)
        {
            _measurements.Clear();
            _measurementsByInstrument.Clear();
            _aggregatedCounters.Clear();
        }
    }

    /// <summary>
    /// Disposes the harness and stops metric collection.
    /// </summary>
    public void Dispose() => _listener.Dispose();
}

/// <summary>
/// Represents a single metric measurement with all associated metadata.
/// </summary>
internal sealed record MetricMeasurement(
    string InstrumentName,
    long Value,
    IReadOnlyDictionary<string, object?> Tags,
    DateTime Timestamp);
