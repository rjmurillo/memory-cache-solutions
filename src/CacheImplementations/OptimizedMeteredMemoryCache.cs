using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;

namespace CacheImplementations;

/// <summary>
/// High-performance <see cref="IMemoryCache"/> decorator that uses atomic operations
/// for minimal-overhead metrics tracking, similar to HybridCache and <see cref="MemoryCache"/>.
/// </summary>
[DebuggerDisplay("{Name ?? \"(unnamed)\"}")]
public sealed class OptimizedMeteredMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly string? _cacheName;
    private readonly bool _enableMetrics;

    // Atomic counters for high-performance metrics
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;
    private long _entryCount;

    // Optional OpenTelemetry counters for periodic publishing
    private readonly Counter<long>? _hitsCounter;
    private readonly Counter<long>? _missesCounter;
    private readonly Counter<long>? _evictionsCounter;

    private volatile bool _disposed;

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
        _enableMetrics = enableMetrics;
        _cacheName = NormalizeCacheName(cacheName);

        if (_enableMetrics)
        {
            // Create OpenTelemetry counters for periodic publishing
            _hitsCounter = meter.CreateCounter<long>("cache_hits_total");
            _missesCounter = meter.CreateCounter<long>("cache_misses_total");
            _evictionsCounter = meter.CreateCounter<long>("cache_evictions_total");
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
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount),
            EvictionCount = Interlocked.Read(ref _evictionCount),
            CurrentEntryCount = Interlocked.Read(ref _entryCount),
            CacheName = _cacheName
        };
    }

    /// <summary>
    /// Publishes accumulated metrics to OpenTelemetry counters.
    /// This can be called periodically by a background service to reduce per-operation overhead.
    /// </summary>
    /// <remarks>
    /// After publishing, the internal counters are reset to zero. This method does nothing if
    /// <c>enableMetrics</c> was <see langword="false"/> during construction or if the instance has been disposed.
    /// </remarks>
    public void PublishMetrics()
    {
        if (!_enableMetrics || _disposed)
            return;

        var stats = GetCurrentStatistics();

        if (stats.HitCount > 0 || stats.MissCount > 0 || stats.EvictionCount > 0)
        {
            var tags = string.IsNullOrEmpty(_cacheName)
                ? default(TagList)
                : new TagList { { "cache.name", _cacheName! } };

            _hitsCounter?.Add(stats.HitCount, tags);
            _missesCounter?.Add(stats.MissCount, tags);
            _evictionsCounter?.Add(stats.EvictionCount, tags);

            // Reset counters after publishing
            Interlocked.Exchange(ref _hitCount, 0);
            Interlocked.Exchange(ref _missCount, 0);
            Interlocked.Exchange(ref _evictionCount, 0);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _inner.CreateEntry(key);

        // Track entry creation
        Interlocked.Increment(ref _entryCount);

        // Register eviction callback
        entry.RegisterPostEvictionCallback(static (key, value, reason, state) =>
        {
            var cache = (OptimizedMeteredMemoryCache)state!;
            Interlocked.Increment(ref cache._evictionCount);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        _inner.Remove(key);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes any remaining metrics before disposal. If <c>disposeInner</c> was <see langword="true"/>
    /// during construction, the underlying cache is also disposed.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Publish any remaining metrics before setting disposed flag
        PublishMetrics();

        _disposed = true;

        if (_disposeInner)
            _inner.Dispose();
    }

    /// <summary>
    /// Normalizes a cache name by trimming whitespace.
    /// </summary>
    /// <param name="name">The cache name to normalize.</param>
    /// <returns>The trimmed cache name, or <see langword="null"/> if the input is <see langword="null"/>, empty, or whitespace.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? NormalizeCacheName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
}
