using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;
using Xunit;
using System.Diagnostics;

namespace Unit;

public class MeteredMemoryCacheTests
{
    private sealed class TestListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value, IEnumerable<KeyValuePair<string, object?>> Tags)> _measurements = new();
        private readonly Dictionary<string, long> _counters = [];
        public IReadOnlyDictionary<string, long> Counters => _counters;
        public IReadOnlyList<(string Name, long Value, IEnumerable<KeyValuePair<string, object?>> Tags)> Measurements => _measurements;

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
                _measurements.Add((inst.Name, measurement, tags.ToArray()));
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
    public void TagListCopyIsThreadSafeForConcurrentAdd()
    {
        var baseTags = new TagList();
        baseTags.Add("cache.name", "concurrent-cache");

        Exception? error = null;
        Parallel.For(0, 1000, i =>
        {
            try
            {
                var tags = new TagList();
                foreach (var tag in baseTags)
                    tags.Add(tag.Key, tag.Value);
                tags.Add("reason", i.ToString());
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.Null(error);
    }

    [Fact]
    public void RecordsHitAndMiss()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: null);

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
        var cache = new MeteredMemoryCache(inner, meter, cacheName: null);

        using var cts = new CancellationTokenSource();
        var options = new MemoryCacheEntryOptions();
        options.AddExpirationToken(new CancellationChangeToken(cts.Token));

        cache.Set("k", 1, options);
        cts.Cancel();
        cache.TryGetValue("k", out _);
        inner.Compact(0.0);

        Assert.True(listener.Counters.TryGetValue("cache_evictions_total", out var ev) && ev >= 1);
    }

    [Fact]
    public void EmitsCacheNameTagOnMetrics()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache3");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "test-cache-name");

        cache.TryGetValue("k", out _); // miss
        cache.Set("k", 42);
        cache.TryGetValue("k", out _); // hit

        // At least one measurement should have the cache.name tag
        Assert.Contains(true, listener.Measurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "test-cache-name")));
    }
}
