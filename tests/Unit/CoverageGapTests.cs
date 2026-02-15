using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Unit;

/// <summary>
/// Tests targeting uncovered code paths to maximize block coverage.
/// </summary>
public class CoverageGapTests
{
    #region CacheStatistics EstimatedSize

    [Fact]
    public void CacheStatistics_EstimatedSize_CanBeSetAndRead()
    {
        var stats = new CacheStatistics
        {
            TotalHits = 10,
            TotalMisses = 5,
            EstimatedSize = 1024,
        };

        Assert.Equal(1024, stats.EstimatedSize);
    }

    [Fact]
    public void CacheStatistics_EstimatedSize_DefaultsToNull()
    {
        var stats = new CacheStatistics();

        Assert.Null(stats.EstimatedSize);
    }

    #endregion

    #region MeteredMemoryCache fallback meter (null IMeterFactory)

    [Fact]
    public void MeteredMemoryCache_NullMeterFactory_CreatesFallbackMeter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cacheName = SharedUtilities.GetUniqueCacheName("fallback");

        using var cache = new MeteredMemoryCache(inner, (IMeterFactory?)null, cacheName);

        Assert.Equal(cacheName, cache.Name);

        // Cache should be functional with fallback meter
        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out var val));
        Assert.Equal("value", val);
    }

    [Fact]
    public void MeteredMemoryCache_NullMeterFactory_WithOptions_CreatesFallbackMeter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cacheName = SharedUtilities.GetUniqueCacheName("fallback-opts");
        var options = new MeteredMemoryCacheOptions { CacheName = cacheName };

        using var cache = new MeteredMemoryCache(inner, (IMeterFactory?)null, options);

        Assert.Equal(cacheName, cache.Name);
        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out _));
    }

    #endregion

    #region cache.estimated_size with TrackStatistics

    [Fact]
    public void MeteredMemoryCache_WithTrackStatistics_RegistersEstimatedSizeGauge()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("track-stats"));
        var cacheName = SharedUtilities.GetUniqueCacheName("track");

        var instrumentNames = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                instrumentNames.Add(inst.Name);
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
        listener.Start();

        using var cache = new MeteredMemoryCache(inner, meter, cacheName);

        Assert.Contains("cache.estimated_size", instrumentNames);
    }

    [Fact]
    public void MeteredMemoryCache_WithTrackStatistics_ReportsEstimatedSize()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions
        {
            TrackStatistics = true,
            SizeLimit = 1000,
        });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("est-size"));

        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name && inst.Name == "cache.estimated_size")
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            measurements.Add(value);
        });
        listener.Start();

        using var cache = new MeteredMemoryCache(inner, meter, SharedUtilities.GetUniqueCacheName("est"));

        // Add entry with size
        using (var entry = cache.CreateEntry("sized-key"))
        {
            entry.Value = "sized-value";
            entry.Size = 42;
        }

        listener.RecordObservableInstruments();

        Assert.Contains(42L, measurements);
    }

    #endregion

    #region Disposal guard in Observable callbacks

    [Fact]
    public void MeteredMemoryCache_DisposalGuard_EstimatedSizeReturnsZeroAfterDispose()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("disposal-guard"));

        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name && inst.Name == "cache.estimated_size")
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            measurements.Add(value);
        });
        listener.Start();

        var cache = new MeteredMemoryCache(inner, meter, SharedUtilities.GetUniqueCacheName("guard"));

        // Dispose cache (but meter is externally owned so Observable callbacks remain)
        cache.Dispose();

        // Trigger Observable instrument read — disposal guard should return 0
        listener.RecordObservableInstruments();

        Assert.All(measurements, m => Assert.Equal(0, m));
    }

    [Fact]
    public void MeteredMemoryCache_DisposalGuard_WithDisposeInner_NoThrow()
    {
        // When using IMeterFactory (externally-owned meter), the Observable callback
        // stays alive after Dispose(). If _disposeInner=true, the inner cache is disposed.
        // The disposal guard must prevent ObjectDisposedException.
        var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("guard-inner"));

        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name && inst.Name == "cache.estimated_size")
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            measurements.Add(value);
        });
        listener.Start();

        var cache = new MeteredMemoryCache(inner, meter, SharedUtilities.GetUniqueCacheName("guard-inner"), disposeInner: true);

        cache.Dispose(); // This disposes inner cache

        // Observable callback should NOT throw ObjectDisposedException
        var ex = Record.Exception(() => listener.RecordObservableInstruments());
        Assert.Null(ex);
        Assert.All(measurements, m => Assert.Equal(0, m));
    }

    #endregion

    #region MeteredMemoryCache with non-null IMeterFactory

    [Fact]
    public void MeteredMemoryCache_NonNullMeterFactory_UsesFactoryMeter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meterFactory = new TestMeterFactory();
        var cacheName = SharedUtilities.GetUniqueCacheName("factory-meter");

        using var cache = new MeteredMemoryCache(inner, meterFactory, cacheName);

        Assert.Equal(cacheName, cache.Name);

        // Perform operations that should emit metrics
        cache.TryGetValue("miss-key", out _); // miss
        cache.Set("hit-key", "value");
        cache.TryGetValue("hit-key", out _); // hit

        // Verify metrics were emitted through the factory-created meter
        var stats = cache.GetCurrentStatistics();
        Assert.Equal(1, stats.TotalHits);
        Assert.Equal(1, stats.TotalMisses);
    }

    [Fact]
    public void MeteredMemoryCache_NonNullMeterFactory_WithOptions_UsesFactoryMeter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meterFactory = new TestMeterFactory();
        var cacheName = SharedUtilities.GetUniqueCacheName("factory-opts");
        var options = new MeteredMemoryCacheOptions { CacheName = cacheName };

        using var cache = new MeteredMemoryCache(inner, meterFactory, options);

        Assert.Equal(cacheName, cache.Name);
        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out _));
    }

    #endregion

    #region Eviction callback after disposal

    [Fact]
    public void MeteredMemoryCache_EvictionCallback_AfterDisposal_NoMetricUpdate()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("evict-after-dispose"));
        var cacheName = SharedUtilities.GetUniqueCacheName("evict-disposed");

        long evictionCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name && inst.Name == "cache.evictions")
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            evictionCount += value;
        });
        listener.Start();

        var cache = new MeteredMemoryCache(inner, meter, cacheName);

        // Use CancellationChangeToken to trigger TokenExpired eviction (which IS counted)
        // Unlike Remove/Replaced, TokenExpired would increment _evictionCount without the disposal guard
        var cts = new CancellationTokenSource();

        // Add entry with expiration token, then dispose cache before eviction fires
        using (var entry = cache.CreateEntry("key"))
        {
            entry.Value = "value";
            entry.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token));
        }

        cache.Dispose();

        // Capture eviction count before forcing eviction
        listener.RecordObservableInstruments();
        var countBeforeEviction = evictionCount;

        // Force eviction via token cancellation (triggers TokenExpired, which IS counted)
        // The disposal guard should prevent the counter increment
        cts.Cancel();

        // Compact to process eviction callbacks
        inner.Compact(1.0);

        // Re-read Observable instruments
        listener.RecordObservableInstruments();

        // Eviction counter should NOT have increased after disposal due to disposal guard
        Assert.Equal(countBeforeEviction, evictionCount);
    }

    #endregion

    #region BuildTags edge cases

    [Fact]
    public void MeteredMemoryCache_AdditionalTags_EmptyKeyAfterTrim_Excluded()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("empty-tag-key"));
        var cacheName = SharedUtilities.GetUniqueCacheName("empty-tag");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = cacheName,
            AdditionalTags = new Dictionary<string, object?>
            {
                { "valid-tag", "value" },
                { "  ", "whitespace-key" }, // This key should be excluded after trim
            },
        };

        var tagSets = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            tagSets.Add(tags.ToArray());
        });
        listener.Start();

        using var cache = new MeteredMemoryCache(inner, meter, options);

        // Perform operation to emit metrics
        cache.Set("key", "val");
        cache.TryGetValue("key", out _);

        // Record Observable instruments
        listener.RecordObservableInstruments();

        // Verify whitespace key is NOT in any tag set
        Assert.NotEmpty(tagSets);
        Assert.All(tagSets, tags =>
            Assert.DoesNotContain(tags, t => t.Key.Trim() == ""));

        // Verify valid-tag IS present
        Assert.Contains(tagSets, tags =>
            tags.Any(t => t.Key == "valid-tag" && (string?)t.Value == "value"));
    }

    [Fact]
    public void MeteredMemoryCache_AdditionalTags_CacheNameDuplicate_Excluded()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("dup-cache-name"));
        var cacheName = SharedUtilities.GetUniqueCacheName("dup-name");

        var options = new MeteredMemoryCacheOptions
        {
            CacheName = cacheName,
            AdditionalTags = new Dictionary<string, object?>
            {
                { "cache.name", "should-be-ignored" },
                { "env", "test" },
            },
        };

        var tagSets = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                ml.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            tagSets.Add(tags.ToArray());
        });
        listener.Start();

        using var cache = new MeteredMemoryCache(inner, meter, options);

        // Perform operation to emit metrics
        cache.TryGetValue("key", out _);

        // Record Observable instruments
        listener.RecordObservableInstruments();

        // Verify cache.name tag has the CORRECT value (from CacheName, not AdditionalTags)
        Assert.NotEmpty(tagSets);
        Assert.All(tagSets, tags =>
        {
            var cacheNameTags = tags.Where(t => t.Key == "cache.name").ToList();
            // Should have exactly one cache.name tag
            Assert.Single(cacheNameTags);
            // The value should be the constructor-specified name, not the duplicate
            Assert.Equal(cacheName, (string?)cacheNameTags[0].Value);
        });

        // Verify env tag IS present
        Assert.Contains(tagSets, tags =>
            tags.Any(t => t.Key == "env" && (string?)t.Value == "test"));
    }

    #endregion

    #region TOCTOU race: ObjectDisposedException catch in estimated_size callback

    [Fact]
    public void MeteredMemoryCache_EstimatedSize_TOCTOU_CatchesObjectDisposedException()
    {
        // When disposeInner=true + externally-owned meter, inner cache is disposed but
        // Observable callbacks remain registered. The try-catch guards this TOCTOU race.
        var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("toctou"));

        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name && inst.Name == "cache.estimated_size")
                ml.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        var cache = new MeteredMemoryCache(inner, meter,
            SharedUtilities.GetUniqueCacheName("toctou"), disposeInner: true);

        // Dispose inner cache directly (simulating the TOCTOU race window)
        inner.Dispose();

        // Now record — the callback should catch ObjectDisposedException and return 0
        var ex = Record.Exception(() => listener.RecordObservableInstruments());
        Assert.Null(ex);
    }

    #endregion

    #region ServiceCollectionExtensions error path

    [Fact]
    public void DecorateMemoryCacheWithMetrics_NoRegistration_Throws()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.DecorateMemoryCacheWithMetrics());

        Assert.Contains("No IMemoryCache registration found", ex.Message);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_MultipleRegistrations_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.DecorateMemoryCacheWithMetrics());

        Assert.Contains("Multiple IMemoryCache registrations found", ex.Message);
    }

    #endregion

    /// <summary>
    /// Minimal IMeterFactory implementation for testing.
    /// </summary>
    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
