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

    #region OptimizedMeteredMemoryCache fallback meter (null IMeterFactory)

    [Fact]
    public void OptimizedMeteredMemoryCache_NullMeterFactory_CreatesFallbackMeter()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        var cacheName = SharedUtilities.GetUniqueCacheName("opt-fallback");

        using var cache = new OptimizedMeteredMemoryCache(inner, (IMeterFactory?)null, cacheName);

        Assert.Equal(cacheName, cache.Name);
        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out var val));
        Assert.Equal("value", val);
    }

    [Fact]
    public void OptimizedMeteredMemoryCache_NullMeterFactory_MetricsDisabled()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());

        using var cache = new OptimizedMeteredMemoryCache(
            inner, (IMeterFactory?)null, enableMetrics: false);

        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out _));
    }

    [Fact]
    public void OptimizedMeteredMemoryCache_IMeterFactory_Constructor_ArgumentValidation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OptimizedMeteredMemoryCache(null!, (IMeterFactory?)null));
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

        Assert.NotEmpty(measurements);
    }

    [Fact]
    public void OptimizedMeteredMemoryCache_WithTrackStatistics_RegistersEstimatedSizeGauge()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("opt-track-stats"));

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

        using var cache = new OptimizedMeteredMemoryCache(inner, meter, SharedUtilities.GetUniqueCacheName("opt-track"));

        Assert.Contains("cache.estimated_size", instrumentNames);
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
    public void OptimizedMeteredMemoryCache_DisposalGuard_EstimatedSizeReturnsZeroAfterDispose()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("opt-disposal-guard"));

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

        var cache = new OptimizedMeteredMemoryCache(inner, meter, SharedUtilities.GetUniqueCacheName("opt-guard"));

        cache.Dispose();

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

    [Fact]
    public void OptimizedMeteredMemoryCache_DisposalGuard_WithDisposeInner_NoThrow()
    {
        var inner = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true });
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("opt-guard-inner"));

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

        var cache = new OptimizedMeteredMemoryCache(inner, meter,
            SharedUtilities.GetUniqueCacheName("opt-guard-inner"), disposeInner: true);

        cache.Dispose();

        var ex = Record.Exception(() => listener.RecordObservableInstruments());
        Assert.Null(ex);
        Assert.All(measurements, m => Assert.Equal(0, m));
    }

    #endregion

    #region OptimizedMeteredMemoryCache constructor variants

    [Fact]
    public void OptimizedMeteredMemoryCache_MeterConstructor_WithMetricsEnabled()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("opt-meter-enabled"));

        var instrumentNames = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                instrumentNames.Add(inst.Name);
            }
        };
        listener.Start();

        using var cache = new OptimizedMeteredMemoryCache(
            inner, meter, enableMetrics: true);

        Assert.Contains("cache.lookups", instrumentNames);
        Assert.Contains("cache.evictions", instrumentNames);
        Assert.Contains("cache.entries", instrumentNames);
    }

    [Fact]
    public void OptimizedMeteredMemoryCache_MeterConstructor_WithMetricsDisabled()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("opt-meter-disabled"));

        var instrumentNames = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, ml) =>
        {
            if (inst.Meter.Name == meter.Name)
            {
                instrumentNames.Add(inst.Name);
            }
        };
        listener.Start();

        using var cache = new OptimizedMeteredMemoryCache(
            inner, meter, enableMetrics: false);

        Assert.DoesNotContain("cache.lookups", instrumentNames);
    }

    [Fact]
    public void OptimizedMeteredMemoryCache_IMeterFactory_WithMetricsEnabled()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meterFactory = new TestMeterFactory();

        using var cache = new OptimizedMeteredMemoryCache(
            inner, meterFactory, SharedUtilities.GetUniqueCacheName("opt-factory"), enableMetrics: true);

        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out _));
    }

    [Fact]
    public void OptimizedMeteredMemoryCache_IMeterFactory_WithMetricsDisabled()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meterFactory = new TestMeterFactory();

        using var cache = new OptimizedMeteredMemoryCache(
            inner, meterFactory, enableMetrics: false);

        cache.Set("key", "value");
        Assert.True(cache.TryGetValue("key", out _));
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
