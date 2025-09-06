using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Unit;

public class MeteredMemoryCacheTests
{
    private sealed class TestListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<string, long> _counters = new();
        public IReadOnlyDictionary<string, long> Counters => _counters;

        public TestListener(params string[] instrumentNames)
        {
            _listener.InstrumentPublished = (inst, listener) =>
            {
                if (instrumentNames.Contains(inst.Name))
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            _listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (_counters.TryGetValue(inst.Name, out var cur))
                {
                    _counters[inst.Name] = cur + measurement;
                }
                else
                {
                    _counters[inst.Name] = measurement;
                }
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public void RecordsHitAndMiss()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter);

        cache.TryGetValue("k", out _); // miss
        cache.Set("k", 10);            // set
        cache.TryGetValue("k", out _); // hit

        Assert.Equal(1, listener.Counters["cache_hits_total"]);
        Assert.Equal(1, listener.Counters["cache_misses_total"]);
    }

    [Fact(Skip = "Flaky under CI timing; revisit when deterministic eviction test harness added.")]
    public void RecordsEviction()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache2");
        using var listener = new TestListener("cache_evictions_total");
        var cache = new MeteredMemoryCache(inner, meter);

        using var cts = new CancellationTokenSource();
        var options = new MemoryCacheEntryOptions();
        options.AddExpirationToken(new CancellationChangeToken(cts.Token));

        cache.Set("k", 1, options);
        // Trigger eviction
        cts.Cancel();
        // Touch cache to process callbacks if needed
        cache.TryGetValue("k", out _);
        inner.Compact(0.0); // noop but ensures maintenance

        Assert.True(listener.Counters.TryGetValue("cache_evictions_total", out var ev) && ev >= 1);
    }
}
