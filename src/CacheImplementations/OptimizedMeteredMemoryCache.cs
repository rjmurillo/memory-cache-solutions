using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CacheImplementations;

/// <summary>
/// High-performance <see cref="IMemoryCache"/> decorator that uses atomic operations
/// for minimal-overhead metrics tracking, similar to HybridCache and <see cref="MemoryCache"/>.
/// Uses Observable instruments per dotnet/runtime#124140 to avoid hot-path overhead.
/// </summary>
[DebuggerDisplay("{Name}")]
public sealed class OptimizedMeteredMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly Meter? _ownedMeter;
    private readonly string _cacheName;
    private readonly KeyValuePair<string, object?>[] _tags;

    // Atomic counters for high-performance metrics
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;
    private long _entryCount;

    // Pre-allocated array for cache.requests observable callback (avoids per-poll allocation)
    private readonly Measurement<long>[] _requestMeasurements = new Measurement<long>[2];

    private int _disposed;

    /// <summary>
    /// Gets the logical name of this cache instance. Defaults to <c>"Default"</c> when no explicit name is provided.
    /// </summary>
    public string Name => _cacheName;

    /// <summary>
    /// Initializes a new instance of <see cref="OptimizedMeteredMemoryCache"/> with high-performance metrics.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> instance to decorate.</param>
    /// <param name="meter">The <see cref="Meter"/> instance used to create metric counters.</param>
    /// <param name="cacheName">Optional logical name for this cache instance. Used as the "cache.name" tag in metrics. Defaults to <c>"Default"</c> when <see langword="null"/>.</param>
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
        _tags = TagBuilder.BuildTags(_cacheName, null);

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
    /// <param name="cacheName">Optional logical name for this cache instance. Used as the "cache.name" tag in metrics. Defaults to <c>"Default"</c> when <see langword="null"/>.</param>
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
        _tags = TagBuilder.BuildTags(_cacheName, null);

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
    /// Initializes a new instance of <see cref="OptimizedMeteredMemoryCache"/> using the options pattern for advanced configuration.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> instance to decorate.</param>
    /// <param name="meter">The <see cref="Meter"/> instance used to create metric counters.</param>
    /// <param name="options">The <see cref="MeteredMemoryCacheOptions"/> containing cache name, disposal behavior, and additional tags.</param>
    /// <param name="enableMetrics">Whether to enable metric collection. When <see langword="false"/>, no metrics are collected.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/>, <paramref name="meter"/>, or <paramref name="options"/> is <see langword="null"/>.</exception>
    public OptimizedMeteredMemoryCache(
        IMemoryCache innerCache,
        Meter meter,
        MeteredMemoryCacheOptions options,
        bool enableMetrics = true)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(options);

        _inner = innerCache;
        _disposeInner = options.DisposeInner;
        _cacheName = NormalizeCacheName(options.CacheName);
        _tags = TagBuilder.BuildTags(_cacheName, options.AdditionalTags);

        if (enableMetrics)
        {
            RegisterObservableInstruments(meter);
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="OptimizedMeteredMemoryCache"/> using an <see cref="IMeterFactory"/> and the options pattern.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> instance to decorate.</param>
    /// <param name="meterFactory">The <see cref="IMeterFactory"/> used to create the <see cref="Meter"/> instance. If <see langword="null"/>, a fallback meter is created and owned by this instance.</param>
    /// <param name="options">The <see cref="MeteredMemoryCacheOptions"/> containing cache name, disposal behavior, and additional tags.</param>
    /// <param name="enableMetrics">Whether to enable metric collection. When <see langword="false"/>, no metrics are collected.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public OptimizedMeteredMemoryCache(
        IMemoryCache innerCache,
        IMeterFactory? meterFactory,
        MeteredMemoryCacheOptions options,
        bool enableMetrics = true)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        ArgumentNullException.ThrowIfNull(options);

        _inner = innerCache;
        _disposeInner = options.DisposeInner;
        _cacheName = NormalizeCacheName(options.CacheName);
        _tags = TagBuilder.BuildTags(_cacheName, options.AdditionalTags);

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
            EstimatedSize = _inner is MemoryCache mc ? mc.GetCurrentStatistics()?.CurrentEstimatedSize : null,
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

        // Entry count is incremented when the entry is committed (disposed), not when created.
        // This prevents inflated counts when entries are created but never committed.

        RegisterEvictionCallback(entry, this);

        return new TrackingCacheEntry(entry, this);
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
    /// Registers a post-eviction callback to track cache evictions atomically.
    /// </summary>
    private static void RegisterEvictionCallback(ICacheEntry entry, OptimizedMeteredMemoryCache self)
    {
        entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
        {
            var cache = (OptimizedMeteredMemoryCache)state!;

            // Guard: no metric updates after disposal
            if (Volatile.Read(ref cache._disposed) != 0)
            {
                return;
            }

            // Per dotnet/runtime#124140: evictions exclude explicit user removals and replacements.
            if (reason != EvictionReason.Removed && reason != EvictionReason.Replaced)
            {
                Interlocked.Increment(ref cache._evictionCount);
            }

            Interlocked.Decrement(ref cache._entryCount);
        }, self);
    }

    /// <summary>
    /// The meter name used per dotnet/runtime#124140.
    /// </summary>
    public const string MeterName = "Microsoft.Extensions.Caching.Memory";

    /// <summary>
    /// Registers Observable instruments that poll atomic counters on demand.
    /// </summary>
    private void RegisterObservableInstruments(Meter meter)
    {
        var tags = _tags;

        // Pre-allocate tag arrays with cache.request.type dimension per OTel conventions
        var hitTags = new KeyValuePair<string, object?>[tags.Length + 1];
        tags.CopyTo(hitTags, 0);
        hitTags[tags.Length] = new KeyValuePair<string, object?>("cache.request.type", "hit");

        var missTags = new KeyValuePair<string, object?>[tags.Length + 1];
        tags.CopyTo(missTags, 0);
        missTags[tags.Length] = new KeyValuePair<string, object?>("cache.request.type", "miss");

        meter.CreateObservableCounter("cache.requests",
            () =>
            {
                _requestMeasurements[0] = new Measurement<long>(Interlocked.Read(ref _hitCount), hitTags);
                _requestMeasurements[1] = new Measurement<long>(Interlocked.Read(ref _missCount), missTags);
                return _requestMeasurements;
            },
            unit: "{requests}",
            description: "Total number of cache lookup operations.");

        meter.CreateObservableCounter("cache.evictions",
            () => new Measurement<long>(Interlocked.Read(ref _evictionCount), tags),
            unit: "{evictions}",
            description: "Total number of automatic cache evictions.");

        meter.CreateObservableUpDownCounter("cache.entries",
            () => new Measurement<long>(Interlocked.Read(ref _entryCount), tags),
            unit: "{entries}",
            description: "Current number of entries in the cache.");

        // cache.estimated_size is only available when the inner cache is MemoryCache with TrackStatistics enabled
        if (_inner is MemoryCache memoryCache && memoryCache.GetCurrentStatistics() is not null)
        {
            meter.CreateObservableGauge("cache.estimated_size",
                () =>
                {
                    if (Volatile.Read(ref _disposed) != 0) return new Measurement<long>(0, tags);
                    try
                    {
                        return new Measurement<long>(memoryCache.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0, tags);
                    }
                    catch (ObjectDisposedException)
                    {
                        // TOCTOU: inner cache may be disposed between the _disposed check and this call
                        // when _disposeInner is true and the meter is externally owned.
                        return new Measurement<long>(0, tags);
                    }
                },
                unit: "By",
                description: "Estimated size of the cache in bytes.");
        }
    }

    /// <summary>
    /// Normalizes a cache name by trimming whitespace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormalizeCacheName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim();
    }

    /// <summary>
    /// Wrapper for <see cref="ICacheEntry"/> that increments entry count only when the entry is committed.
    /// This prevents inflated counts when entries are created but never committed to the cache.
    /// </summary>
    private sealed class TrackingCacheEntry : ICacheEntry
    {
        private readonly ICacheEntry _inner;
        private readonly OptimizedMeteredMemoryCache _cache;
        private int _committed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingCacheEntry"/> class.
        /// </summary>
        /// <param name="inner">The underlying cache entry to wrap.</param>
        /// <param name="cache">The parent cache instance for entry count tracking.</param>
        public TrackingCacheEntry(ICacheEntry inner, OptimizedMeteredMemoryCache cache)
        {
            _inner = inner;
            _cache = cache;
        }

        /// <inheritdoc/>
        public object Key => _inner.Key;

        /// <inheritdoc/>
        public object? Value
        {
            get => _inner.Value;
            set => _inner.Value = value;
        }

        /// <inheritdoc/>
        public DateTimeOffset? AbsoluteExpiration
        {
            get => _inner.AbsoluteExpiration;
            set => _inner.AbsoluteExpiration = value;
        }

        /// <inheritdoc/>
        public TimeSpan? AbsoluteExpirationRelativeToNow
        {
            get => _inner.AbsoluteExpirationRelativeToNow;
            set => _inner.AbsoluteExpirationRelativeToNow = value;
        }

        /// <inheritdoc/>
        public TimeSpan? SlidingExpiration
        {
            get => _inner.SlidingExpiration;
            set => _inner.SlidingExpiration = value;
        }

        /// <inheritdoc/>
        public IList<IChangeToken> ExpirationTokens => _inner.ExpirationTokens;

        /// <inheritdoc/>
        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => _inner.PostEvictionCallbacks;

        /// <inheritdoc/>
        public CacheItemPriority Priority
        {
            get => _inner.Priority;
            set => _inner.Priority = value;
        }

        /// <inheritdoc/>
        public long? Size
        {
            get => _inner.Size;
            set => _inner.Size = value;
        }

        /// <summary>
        /// Commits the entry to the cache and increments the entry count.
        /// The count is only incremented once, even if Dispose is called multiple times.
        /// </summary>
        public void Dispose()
        {
            // Only increment entry count once when the entry is committed
            if (Interlocked.Exchange(ref _committed, 1) == 0)
            {
                Interlocked.Increment(ref _cache._entryCount);
            }

            _inner.Dispose();
        }
    }
}
