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

    [Fact]
    public void OptionsConstructor_WithCacheName_SetsNameProperty()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache4");
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "named-cache"
        };

        var cache = new MeteredMemoryCache(inner, meter, options);

        Assert.Equal("named-cache", cache.Name);
    }

    [Fact]
    public void OptionsConstructor_WithNullCacheName_NamePropertyIsNull()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache5");
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = null
        };

        var cache = new MeteredMemoryCache(inner, meter, options);

        Assert.Null(cache.Name);
    }

    [Fact]
    public void OptionsConstructor_WithEmptyCacheName_NamePropertyIsNull()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache6");
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = ""
        };

        var cache = new MeteredMemoryCache(inner, meter, options);

        Assert.Null(cache.Name);
    }

    [Fact]
    public void OptionsConstructor_WithAdditionalTags_EmitsAllTagsOnMetrics()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache7");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "tagged-cache",
            AdditionalTags = { ["environment"] = "test", ["region"] = "us-west-2" }
        };

        var cache = new MeteredMemoryCache(inner, meter, options);

        cache.TryGetValue("k", out _); // miss
        cache.Set("k", 100);
        cache.TryGetValue("k", out _); // hit

        // Verify cache.name tag is present
        Assert.Contains(true, listener.Measurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "tagged-cache")));

        // Verify additional tags are present
        Assert.Contains(true, listener.Measurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "environment" && (string?)tag.Value == "test")));
        Assert.Contains(true, listener.Measurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "region" && (string?)tag.Value == "us-west-2")));
    }

    [Fact]
    public void OptionsConstructor_WithDuplicateCacheNameTag_CacheNameTakesPrecedence()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache8");
        using var listener = new TestListener("cache_hits_total");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "primary-cache",
            AdditionalTags = { ["cache.name"] = "duplicate-cache" }
        };

        var cache = new MeteredMemoryCache(inner, meter, options);

        cache.TryGetValue("k", out _); // miss

        // Should only have the cache.name from CacheName property, not from AdditionalTags
        var measurements = listener.Measurements.Where(m => m.Name == "cache_hits_total" || m.Name == "cache_misses_total").ToList();
        Assert.All(measurements, m =>
        {
            var cacheNameTags = m.Tags.Where(tag => tag.Key == "cache.name").ToList();
            Assert.Single(cacheNameTags); // Only one cache.name tag
            Assert.Equal("primary-cache", cacheNameTags[0].Value);
        });
    }

    [Fact]
    public void OptionsConstructor_WithDisposeInner_DisposesInnerCacheOnDispose()
    {
        var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache9");
        var options = new MeteredMemoryCacheOptions
        {
            DisposeInner = true
        };

        var cache = new MeteredMemoryCache(inner, meter, options);
        cache.Dispose();

        // Verify inner cache was disposed by checking it throws when accessed
        Assert.Throws<ObjectDisposedException>(() => inner.TryGetValue("test", out _));
    }

    [Fact]
    public void OptionsConstructor_WithoutDisposeInner_DoesNotDisposeInnerCache()
    {
        var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache10");
        var options = new MeteredMemoryCacheOptions
        {
            DisposeInner = false
        };

        var cache = new MeteredMemoryCache(inner, meter, options);
        cache.Dispose();

        // Verify inner cache was not disposed
        inner.Set("test", "value");
        Assert.True(inner.TryGetValue("test", out _));

        // Clean up
        inner.Dispose();
    }

    [Fact]
    public void StringConstructor_WithCacheName_SetsNameProperty()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache11");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "string-cache");

        Assert.Equal("string-cache", cache.Name);
    }

    [Fact]
    public void StringConstructor_WithNullCacheName_NamePropertyIsNull()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache12");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: null);

        Assert.Null(cache.Name);
    }

    [Fact]
    public void StringConstructor_WithEmptyCacheName_NamePropertyIsNull()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache13");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "");

        Assert.Null(cache.Name);
    }

    [Fact]
    public void StringConstructor_WithDisposeInner_DisposesInnerCacheOnDispose()
    {
        var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache14");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "dispose-test", disposeInner: true);
        cache.Dispose();

        // Verify inner cache was disposed
        Assert.Throws<ObjectDisposedException>(() => inner.TryGetValue("test", out _));
    }

    [Fact]
    public void StringConstructor_WithoutDisposeInner_DoesNotDisposeInnerCache()
    {
        var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache15");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "no-dispose-test", disposeInner: false);
        cache.Dispose();

        // Verify inner cache was not disposed
        inner.Set("test", "value");
        Assert.True(inner.TryGetValue("test", out _));

        // Clean up
        inner.Dispose();
    }

    [Fact]
    public void TryGet_WithNamedCache_RecordsMetricsWithCacheName()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache16");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "tryget-cache");

        // Test miss
        var missResult = cache.TryGet<string>("missing-key", out var missValue);
        Assert.False(missResult);
        Assert.Null(missValue);

        // Test hit
        cache.Set("present-key", "test-value");
        var hitResult = cache.TryGet<string>("present-key", out var hitValue);
        Assert.True(hitResult);
        Assert.Equal("test-value", hitValue);

        // Verify metrics with cache.name tag
        Assert.Equal(1, listener.Counters["cache_hits_total"]);
        Assert.Equal(1, listener.Counters["cache_misses_total"]);

        Assert.Contains(true, listener.Measurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "tryget-cache")));
    }

    [Fact]
    public void GetOrCreate_WithNamedCache_RecordsMetricsWithCacheName()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache17");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var cache = new MeteredMemoryCache(inner, meter, cacheName: "getorcreate-cache");

        // First call should be a miss and create
        var value1 = cache.GetOrCreate("key1", entry => "created-value");
        Assert.Equal("created-value", value1);

        // Second call should be a hit
        var value2 = cache.GetOrCreate("key1", entry => "should-not-be-called");
        Assert.Equal("created-value", value2);

        // Verify metrics
        Assert.Equal(1, listener.Counters["cache_hits_total"]);
        Assert.Equal(1, listener.Counters["cache_misses_total"]);

        // Verify cache.name tag is present
        Assert.Contains(true, listener.Measurements.Select(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "getorcreate-cache")));
    }

    [Fact]
    public void MultipleNamedCaches_EmitSeparateMetrics()
    {
        using var inner1 = new MemoryCache(new MemoryCacheOptions());
        using var inner2 = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache18");
        using var listener = new TestListener("cache_hits_total", "cache_misses_total");

        var cache1 = new MeteredMemoryCache(inner1, meter, cacheName: "cache-one");
        var cache2 = new MeteredMemoryCache(inner2, meter, cacheName: "cache-two");

        // Generate metrics for both caches
        cache1.TryGetValue("key", out _); // miss for cache-one
        cache2.TryGetValue("key", out _); // miss for cache-two

        cache1.Set("key", "value1");
        cache2.Set("key", "value2");

        cache1.TryGetValue("key", out _); // hit for cache-one
        cache2.TryGetValue("key", out _); // hit for cache-two

        // Verify separate metrics for each cache
        var cache1Measurements = listener.Measurements.Where(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "cache-one")).ToList();
        var cache2Measurements = listener.Measurements.Where(m =>
            m.Tags.Any(tag => tag.Key == "cache.name" && (string?)tag.Value == "cache-two")).ToList();

        Assert.NotEmpty(cache1Measurements);
        Assert.NotEmpty(cache2Measurements);

        // Each cache should have both hit and miss measurements
        Assert.Contains(cache1Measurements, m => m.Name == "cache_hits_total");
        Assert.Contains(cache1Measurements, m => m.Name == "cache_misses_total");
        Assert.Contains(cache2Measurements, m => m.Name == "cache_hits_total");
        Assert.Contains(cache2Measurements, m => m.Name == "cache_misses_total");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_NullabilityValidation_NullAndEmpty(string? invalidInput)
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache19");

        // Should not throw for null/empty cache names and Name should be null
        var cache = new MeteredMemoryCache(inner, meter, cacheName: invalidInput);
        Assert.Null(cache.Name);
    }

    [Fact]
    public void Constructor_NullabilityValidation_WhitespaceString()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache19b");

        // Whitespace-only strings are treated as valid cache names (not null)
        var cache = new MeteredMemoryCache(inner, meter, cacheName: "   ");
        Assert.Equal("   ", cache.Name);
    }

    [Fact]
    public void Constructor_ArgumentNullException_Validation()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var meter = new Meter("test.metered.cache19c");

        // Should throw for null required parameters
        Assert.Throws<ArgumentNullException>(() => new MeteredMemoryCache(null!, meter, cacheName: "test"));
        Assert.Throws<ArgumentNullException>(() => new MeteredMemoryCache(inner, null!, cacheName: "test"));

        var options = new MeteredMemoryCacheOptions();
        Assert.Throws<ArgumentNullException>(() => new MeteredMemoryCache(null!, meter, options));
        Assert.Throws<ArgumentNullException>(() => new MeteredMemoryCache(inner, null!, options));
        Assert.Throws<ArgumentNullException>(() => new MeteredMemoryCache(inner, meter, null!));
    }
}
