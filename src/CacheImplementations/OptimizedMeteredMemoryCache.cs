using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;

namespace CacheImplementations;

/// <summary>
/// High-performance <see cref="IMemoryCache"/> decorator that uses atomic operations
/// for minimal-overhead metrics tracking, similar to HybridCache and <see cref="MemoryCache"/>.
/// Uses Observable instruments per dotnet/runtime#124140 to avoid hot-path overhead.
/// </summary>("{Name ?? \"(unnamed)\"}")]
public sealed class OptimizedMeteredMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly Meter? _ownedMeter;
    private readonly string? _cacheName;

    // Atomic counters for high-performance metrics
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;
    private long _entryCount;

    private int _disposed;

    /// <summary>
    /// Gets the logical name of this cache instance, if provided.
    /// </summary>
    public string? Name => _cacheName;

    /// <summary>
    /// Initializes a new instance of <see cref="OptimizedMeteredMemoryCache"/> with high-performance metrics.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> instance to decorate.</param>
    /// <param name="meter">The <see cref="Meter"/> instance used to create metric counters.</param>
    /// <param name="cacheName">Optional logical name for this cache instance. Used as the "cache.name" tag in metrics.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed.</param>
    /// <param name="enableMetrics">Whether to enable metric collection. When <see langword="false"/>, no metrics are collected.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/> or <paramref name="meter"/> is <see langword="null"/>.</exception>
    public OptimizedMeteredMemoryCache(
        IMemoryCache innerCache,
        Meter meter,
        string? cacheName = null,
        bool disposeInner = false,
        bool enableMetrics = true)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        ArgumentNullException.ThrowIfNull(meter);

        _inner = innerCache;
        _disposeInner = disposeInner;
        _cacheName = NormalizeCacheName(cacheName);

        if (enableMetrics)
        {
            RegisterObservableInstruments(meter);
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="OptimizedMeteredMemoryCache"/> using an <see cref="IMeterFactory"/> for proper meter lifecycle management.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> instance to decorate.</param>
    /// <param name="meterFactory">The <see cref="IMeterFactory"/> used to create the <see cref="Meter"/> instance. If <see langword="null"/>, a fallback meter is created and owned by this instance.</param>
    /// <param name="cacheName">Optional logical name for this cache instance. Used as the "cache.name" tag in metrics.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed.</param>
    /// <param name="enableMetrics">Whether to enable metric collection. When <see langword="false"/>, no metrics are collected.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/> is <see langword="null"/>.</exception>
    public OptimizedMeteredMemoryCache(
        IMemoryCache innerCache,
        IMeterFactory? meterFactory,
        string? cacheName = null,
        bool disposeInner = false,
        bool enableMetrics = true)
    {
        ArgumentNullException.ThrowIfNull(innerCache);

        _inner = innerCache;
        _disposeInner = disposeInner;
        _cacheName = NormalizeCacheName(cacheName);

        // Create meter - if factory is null, we own the meter and must dispose it
        if (enableMetrics)
        {
            Meter meter;
            if (meterFactory is not null)
            {
                meter = meterFactory.Create(MeterName);
                _ownedMeter = null;
            }
            else
            {
                meter = new Meter(MeterName);
                _ownedMeter = meter;
            }

            RegisterObservableInstruments(meter);
        }
    }

    /// <summary>
    /// Gets current cache statistics using atomic reads, similar to <see cref="MemoryCache.GetCurrentStatistics()"/>.
    /// </summary>
    /// <returns>A <see cref="CacheStatistics"/> instance containing current cache metrics.</returns>
    public CacheStatistics GetCurrentStatistics()
    {
        return new CacheStatistics
        {
            TotalHits = Interlocked.Read(ref _hitCount),
            TotalMisses = Interlocked.Read(ref _missCount),
            TotalEvictions = Interlocked.Read(ref _evictionCount),
            CurrentEntryCount = Interlocked.Read(ref _entryCount),
        };
    }

    /// <summary>
    /// Retained for backward compatibility. With Observable instruments, metrics are reported
    /// automatically by the metrics system — calling this method is no longer necessary.
    /// </summary>
    public void PublishMetrics()
    {
        // No-op: Observable instruments read atomic counters directly
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var hit = _inner.TryGetValue(key, out value);

        // Use atomic increment for minimal overhead
        if (hit)
            Interlocked.Increment(ref _hitCount);
        else
            Interlocked.Increment(ref _missCount);

        return hit;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public ICacheEntry CreateEntry(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var entry = _inner.CreateEntry(key);

        // Track entry creation
        Interlocked.Increment(ref _entryCount);

        // Register eviction callback
        entry.RegisterPostEvictionCallback(static (key, value, reason, state) =>
        {
            var cache = (OptimizedMeteredMemoryCache)state!;

            // Per dotnet/runtime#124140: evictions exclude explicit user removals and replacements.
            if (reason != EvictionReason.Removed && reason != EvictionReason.Replaced)
            {
                Interlocked.Increment(ref cache._evictionCount);
            }

            Interlocked.Decrement(ref cache._entryCount);
        }, this);

        return entry;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public void Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        _inner.Remove(key);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Dispose the owned meter first to unregister Observable instruments
        // and break the reference chain (Meter → Instruments → Callbacks → this)
        _ownedMeter?.Dispose();

        if (_disposeInner)
            _inner.Dispose();
    }

    /// <summary>
    /// The meter name used per dotnet/runtime#124140.
    /// </summary>
    internal const string MeterName = "Microsoft.Extensions.Caching.Memory";

    /// <summary>
    /// Registers Observable instruments that poll atomic counters on demand.
    /// </summary>
    private void RegisterObservableInstruments(Meter meter)
    {
        var tags = string.IsNullOrEmpty(_cacheName)
            ? Array.Empty<KeyValuePair<string, object?>>()
            : new[] { new KeyValuePair<string, object?>("cache.name", _cacheName!) };

        // Pre-allocate tag arrays with cache.result dimension per OTel conventions
        var hitTags = tags.Append(new KeyValuePair<string, object?>("cache.result", "hit")).ToArray();
        var missTags = tags.Append(new KeyValuePair<string, object?>("cache.result", "miss")).ToArray();

        meter.CreateObservableCounter("cache.lookups",
            () => new[]
            {
                new Measurement<long>(Interlocked.Read(ref _hitCount), hitTags),
                new Measurement<long>(Interlocked.Read(ref _missCount), missTags),
            },
            description: "Total number of cache lookup operations.");

        meter.CreateObservableCounter("cache.evictions",
            () => new Measurement<long>(Interlocked.Read(ref _evictionCount), tags),
            description: "Total number of automatic cache evictions.");

        meter.CreateObservableUpDownCounter("cache.entries",
            () => new Measurement<long>(Interlocked.Read(ref _entryCount), tags),
            description: "Current number of entries in the cache.");

        // cache.estimated_size is only available when the inner cache is MemoryCache with TrackStatistics enabled
        if (_inner is MemoryCache memoryCache)
        {
            meter.CreateObservableGauge("cache.estimated_size",
                () => new Measurement<long>(memoryCache.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0, tags),
                description: "Estimated size of the cache in bytes.");
        }
    }

    /// <summary>
    /// Normalizes a cache name by trimming whitespace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? NormalizeCacheName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
}
