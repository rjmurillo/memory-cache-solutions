using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CacheImplementations;

/// <summary>
/// <see cref="IMemoryCache"/> decorator that emits OpenTelemetry / .NET metrics for cache hits, misses and evictions.
/// Provides comprehensive observability for any <see cref="IMemoryCache"/> implementation.
/// Uses Observable instruments per dotnet/runtime#124140 to avoid hot-path overhead:
///  - cache.requests (<see cref="ObservableCounter{T}"/>) with cache.request.type dimension (hit/miss)
///  - cache.evictions (<see cref="ObservableCounter{T}"/>)
///  - cache.entries (<see cref="ObservableUpDownCounter{T}"/>)
///  - cache.estimated_size (<see cref="ObservableGauge{T}"/>) when the inner cache is a <see cref="MemoryCache"/> with statistics tracking enabled.
/// </summary>
[DebuggerDisplay("{Name ?? \"(unnamed)\"}")]
public sealed class MeteredMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly Meter? _ownedMeter;

    // Pre-allocated tags for Observable instrument callbacks (zero per-operation allocation)
    private readonly KeyValuePair<string, object?>[] _tags;

    // Atomic counters for high-performance metrics (no per-operation allocation)
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;
    private long _entryCount;

    private int _disposed;

    /// <summary>
    /// Gets the logical name of this cache instance, if provided.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MeteredMemoryCache"/> that decorates the specified <see cref="IMemoryCache"/> with OpenTelemetry metrics.
    /// </summary>
    /// <param name="innerCache">The <see cref="IMemoryCache"/> implementation to decorate with metrics.</param>
    /// <param name="meter">The <see cref="Meter"/> instance used to create counters for hit, miss, and eviction metrics.</param>
    /// <param name="cacheName">Optional logical name for this cache instance. Used as the "cache.name" tag in dimensional metrics. Pass <see langword="null"/> for unnamed cache.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed. Defaults to <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/> or <paramref name="meter"/> is <see langword="null"/>.</exception>
    public MeteredMemoryCache(IMemoryCache innerCache, Meter meter, string? cacheName = null, bool disposeInner = false)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        ArgumentNullException.ThrowIfNull(meter);
        _inner = innerCache;
        _disposeInner = disposeInner;

        var normalizedCacheName = NormalizeCacheName(cacheName);
        Name = normalizedCacheName;

        // Pre-allocate tags array for Observable instrument callbacks
        _tags = BuildTags(normalizedCacheName, null);

        // Create Observable instruments per dotnet/runtime#124140 — zero hot-path overhead
        RegisterObservableInstruments(meter);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MeteredMemoryCache"/> using an <see cref="IMeterFactory"/> for proper meter lifecycle management.
    /// </summary>
    /// <param name="innerCache">The <see cref="IMemoryCache"/> implementation to decorate with metrics.</param>
    /// <param name="meterFactory">The <see cref="IMeterFactory"/> used to create the <see cref="Meter"/> instance. If <see langword="null"/>, a fallback meter is created and owned by this instance.</param>
    /// <param name="cacheName">Optional logical name for this cache instance. Used as the "cache.name" tag in dimensional metrics.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed. Defaults to <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/> is <see langword="null"/>.</exception>
    public MeteredMemoryCache(IMemoryCache innerCache, IMeterFactory? meterFactory, string? cacheName = null, bool disposeInner = false)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        _inner = innerCache;
        _disposeInner = disposeInner;

        var normalizedCacheName = NormalizeCacheName(cacheName);
        Name = normalizedCacheName;

        // Pre-allocate tags array for Observable instrument callbacks
        _tags = BuildTags(normalizedCacheName, null);

        // Create meter - if factory is null, we own the meter and must dispose it
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

        // Create Observable instruments per dotnet/runtime#124140 — zero hot-path overhead
        RegisterObservableInstruments(meter);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MeteredMemoryCache"/> using the options pattern for advanced configuration.
    /// </summary>
    /// <param name="innerCache">The <see cref="IMemoryCache"/> implementation to decorate with metrics.</param>
    /// <param name="meter">The <see cref="Meter"/> instance used to create counters for hit, miss, and eviction metrics.</param>
    /// <param name="options">The <see cref="MeteredMemoryCacheOptions"/> containing cache name, disposal behavior, and additional tags.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/>, <paramref name="meter"/>, or <paramref name="options"/> is <see langword="null"/>.</exception>
    public MeteredMemoryCache(IMemoryCache innerCache, Meter meter, MeteredMemoryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(options);
        _inner = innerCache;
        _disposeInner = options.DisposeInner;

        var normalizedCacheName = NormalizeCacheName(options.CacheName);
        Name = normalizedCacheName;

        // Pre-allocate tags array with cache name and additional tags
        _tags = BuildTags(normalizedCacheName, options.AdditionalTags);

        // Create Observable instruments per dotnet/runtime#124140 — zero hot-path overhead
        RegisterObservableInstruments(meter);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MeteredMemoryCache"/> using an <see cref="IMeterFactory"/> and the options pattern.
    /// </summary>
    /// <param name="innerCache">The <see cref="IMemoryCache"/> implementation to decorate with metrics.</param>
    /// <param name="meterFactory">The <see cref="IMeterFactory"/> used to create the <see cref="Meter"/> instance. If <see langword="null"/>, a fallback meter is created and owned by this instance.</param>
    /// <param name="options">The <see cref="MeteredMemoryCacheOptions"/> containing cache name, disposal behavior, and additional tags.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerCache"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public MeteredMemoryCache(IMemoryCache innerCache, IMeterFactory? meterFactory, MeteredMemoryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(innerCache);
        ArgumentNullException.ThrowIfNull(options);
        _inner = innerCache;
        _disposeInner = options.DisposeInner;

        var normalizedCacheName = NormalizeCacheName(options.CacheName);
        Name = normalizedCacheName;

        // Pre-allocate tags array with cache name and additional tags
        _tags = BuildTags(normalizedCacheName, options.AdditionalTags);

        // Create meter - if factory is null, we own the meter and must dispose it
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

        // Create Observable instruments per dotnet/runtime#124140 — zero hot-path overhead
        RegisterObservableInstruments(meter);
    }

    /// <summary>
    /// Attempts to get a value associated with the specified key from the cache and records hit/miss metrics.
    /// </summary>
    /// <param name="key">The cache key to retrieve. Cannot be <see langword="null"/>.</param>
    /// <param name="value">When this method returns, contains the cached value if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the key was found in the cache; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var hit = _inner.TryGetValue(key, out value);

        // Atomic increment only — Observable instruments poll these values on demand
        if (hit)
            Interlocked.Increment(ref _hitCount);
        else
            Interlocked.Increment(ref _missCount);

        return hit;
    }

    /// <summary>
    /// Creates a new <see cref="ICacheEntry"/> instance associated with the specified key, with automatic eviction callback registration.
    /// </summary>
    /// <param name="key">The cache key for the entry. Cannot be <see langword="null"/>.</param>
    /// <returns>The newly created <see cref="ICacheEntry"/> instance with pre-registered eviction tracking.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
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

    /// <summary>
    /// Removes the cache entry associated with the specified key.
    /// </summary>
    /// <param name="key">The cache key to remove. Cannot be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
    public void Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _inner.Remove(key);
    }

    /// <summary>
    /// The meter name used per dotnet/runtime#124140.
    /// </summary>
    public const string MeterName = "Microsoft.Extensions.Caching.Memory";

    /// <summary>
    /// Normalizes cache names to handle whitespace and prevent tag cardinality issues.
    /// </summary>
    private static string NormalizeCacheName(string? cacheName)
    {
        if (string.IsNullOrEmpty(cacheName))
            return "Default";

        var trimmed = cacheName.Trim();
        return string.IsNullOrEmpty(trimmed) ? "Default" : trimmed;
    }

    /// <summary>
    /// Builds a pre-allocated tag array for Observable instrument callbacks.
    /// </summary>
    private static KeyValuePair<string, object?>[] BuildTags(
        string cacheName,
        IDictionary<string, object?>? additionalTags)
    {
        var tagList = new List<KeyValuePair<string, object?>>();

        if (!string.IsNullOrEmpty(cacheName))
        {
            tagList.Add(new KeyValuePair<string, object?>("cache.name", cacheName));
        }

        if (additionalTags != null)
        {
#pragma warning disable S3267 // Intentionally avoiding LINQ Where() allocation for performance
            foreach (var kvp in additionalTags)
            {
                if (!string.Equals(kvp.Key, "cache.name", StringComparison.Ordinal))
                {
                    var normalizedKey = kvp.Key?.Trim();
                    if (!string.IsNullOrEmpty(normalizedKey))
                    {
                        tagList.Add(new KeyValuePair<string, object?>(normalizedKey, kvp.Value));
                    }
                }
            }
#pragma warning restore S3267
        }

        return tagList.ToArray();
    }

    /// <summary>
    /// Registers Observable instruments that poll atomic counters on demand.
    /// Per dotnet/runtime#124140, all instruments are Observable to avoid hot-path overhead.
    /// </summary>
    private void RegisterObservableInstruments(Meter meter)
    {
        var tags = _tags;

        // Pre-allocate tag arrays with cache.request.type dimension per OTel conventions
        var hitTags = tags.Append(new KeyValuePair<string, object?>("cache.request.type", "hit")).ToArray();
        var missTags = tags.Append(new KeyValuePair<string, object?>("cache.request.type", "miss")).ToArray();

        meter.CreateObservableCounter("cache.requests",
            () => new[]
            {
                new Measurement<long>(Interlocked.Read(ref _hitCount), hitTags),
                new Measurement<long>(Interlocked.Read(ref _missCount), missTags),
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
    /// Registers a post-eviction callback to track cache evictions atomically.
    /// </summary>
    private static void RegisterEvictionCallback(ICacheEntry entry, MeteredMemoryCache self)
    {
        entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
        {
            var cache = (MeteredMemoryCache)state!;

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
    /// Releases all resources used by this <see cref="MeteredMemoryCache"/> instance.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Dispose the owned meter first to unregister Observable instruments
        // and break the reference chain (Meter → Instruments → Callbacks → this)
        _ownedMeter?.Dispose();

        if (_disposeInner)
        {
            _inner.Dispose();
        }
    }

    /// <summary>
    /// Wrapper for <see cref="ICacheEntry"/> that increments entry count only when the entry is committed.
    /// This prevents inflated counts when entries are created but never committed to the cache.
    /// </summary>
    private sealed class TrackingCacheEntry : ICacheEntry
    {
        private readonly ICacheEntry _inner;
        private readonly MeteredMemoryCache _cache;
        private int _committed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingCacheEntry"/> class.
        /// </summary>
        /// <param name="inner">The underlying cache entry to wrap.</param>
        /// <param name="cache">The parent cache instance for entry count tracking.</param>
        public TrackingCacheEntry(ICacheEntry inner, MeteredMemoryCache cache)
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
