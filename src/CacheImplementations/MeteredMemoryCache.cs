using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;

namespace CacheImplementations;

/// <summary>
/// <see cref="IMemoryCache"/> decorator that emits OpenTelemetry / .NET metrics for cache hits, misses and evictions.
/// Provides comprehensive observability for any <see cref="IMemoryCache"/> implementation.
/// Instruments:
///  - cache_hits_total (<see cref="Counter{T}"/> where T is <see langword="long"/>)
///  - cache_misses_total (<see cref="Counter{T}"/> where T is <see langword="long"/>)
///  - cache_evictions_total (<see cref="Counter{T}"/> where T is <see langword="long"/>) with tag "reason" = <see cref="EvictionReason"/> string
/// </summary>
public sealed class MeteredMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _evictions;
    private readonly TagList _baseTags;
    private bool _disposed;

    /// <summary>
    /// Gets the logical name of this cache instance, if provided.
    /// </summary>
    public string? Name { get; }

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

        // Create OpenTelemetry counters using standardized metric names that align with observability conventions
        // These counters will emit to any registered metric exporters (OTLP, Prometheus, console, etc.)
        _hits = meter.CreateCounter<long>("cache_hits_total");
        _misses = meter.CreateCounter<long>("cache_misses_total");
        _evictions = meter.CreateCounter<long>("cache_evictions_total");

        // Initialize TagList for dimensional metrics - this will be used as immutable base for thread-safe copies
        // TagList is used for high-performance metric tagging with minimal allocations
        _baseTags = new TagList();
        if (!string.IsNullOrEmpty(cacheName))
        {
            // Add cache.name as a dimensional tag to distinguish metrics from multiple cache instances
            // This enables filtering and aggregation by cache name in monitoring dashboards
            _baseTags.Add("cache.name", cacheName);
            Name = cacheName;
        }
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

        // Create standardized metric counters - names follow OpenTelemetry semantic conventions
        _hits = meter.CreateCounter<long>("cache_hits_total");
        _misses = meter.CreateCounter<long>("cache_misses_total");
        _evictions = meter.CreateCounter<long>("cache_evictions_total");

        // Build dimensional tag list for all metric emissions
        _baseTags = new TagList();
        if (!string.IsNullOrEmpty(options.CacheName))
        {
            _baseTags.Add("cache.name", options.CacheName);
            Name = options.CacheName;
        }

        // Add user-defined custom tags while preventing cache.name override
        // This filtering ensures cache.name remains consistent if set via CacheName property
        // Note: Using explicit foreach instead of LINQ Where() to avoid allocation overhead
        // as identified in PR feedback for high-performance metric emission scenarios
#pragma warning disable S3267 // Intentionally avoiding LINQ Where() allocation
        foreach (var kvp in options.AdditionalTags)
        {
            // Skip cache.name to prevent override of the value set from CacheName property
            if (!string.Equals(kvp.Key, "cache.name", System.StringComparison.Ordinal))
            {
                _baseTags.Add(kvp.Key, kvp.Value);
            }
        }
#pragma warning restore S3267
    }

    /// <summary>
    /// Attempts to get a strongly typed value from the cache and records hit/miss metrics.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to retrieve. Cannot be <see langword="null"/>.</param>
    /// <param name="value">When this method returns, contains the cached value if found and of the correct type; otherwise, the default value for <typeparamref name="T"/>.</param>
    /// <returns><see langword="true"/> if the key was found and the value is of type <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
    public bool TryGet<T>(object key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inner.TryGetValue(key, out var obj) && obj is T t)
        {
            _hits.Add(1, CreateBaseTags(_baseTags));
            value = t;
            return true;
        }
        _misses.Add(1, CreateBaseTags(_baseTags));
        value = default!;
        return false;
    }

    /// <summary>
    /// Sets a cache entry with the specified key and value, automatically registering an eviction callback to emit eviction metrics.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <param name="options">The <see cref="MemoryCacheEntryOptions"/> for the cache entry. If <see langword="null"/>, a new instance will be created.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
    public void Set<T>(object key, T value, MemoryCacheEntryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new MemoryCacheEntryOptions();

        // Register a static callback to track evictions - critical for comprehensive cache observability
        // Using 'static' delegate avoids capturing 'this' directly, preventing memory leaks in long-lived caches
        // The callback is invoked asynchronously when the entry is removed for any reason (expiry, capacity, manual removal)
        options.RegisterPostEvictionCallback(static (_, _, reason, state) =>
        {
            var self = (MeteredMemoryCache)state!;
            if (!self._disposed)
            {
                // Create a new TagList with eviction reason to maintain thread safety
                // Each eviction gets its own tag collection to prevent concurrent modification issues
                var tags = CreateEvictionTags(self._baseTags, reason.ToString());
                self._evictions.Add(1, tags);
            }
        }, this);
        _inner.Set(key, value, options);
    }

    /// <summary>
    /// Gets an existing cache entry or creates a new one using the provided factory, while emitting hit/miss metrics and registering eviction callbacks.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key. Cannot be <see langword="null"/>.</param>
    /// <param name="factory">The factory function to create a new cache entry if the key is not found. Cannot be <see langword="null"/>.</param>
    /// <returns>The cached value if found, or the newly created value from the factory.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the factory returns <see langword="null"/> for a reference type.</exception>
    public T GetOrCreate<T>(object key, Func<ICacheEntry, T> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // First attempt: check if key exists and value is correct type (cache hit scenario)
        if (_inner.TryGetValue(key, out var existing) && existing is T hit)
        {
            _hits.Add(1, CreateBaseTags(_baseTags));
            return hit;
        }

        // Cache miss: record miss metric and delegate to inner cache for creation
        // This ensures accurate hit/miss ratios even with concurrent access patterns
        _misses.Add(1, CreateBaseTags(_baseTags));
        var created = _inner.GetOrCreate(key, entry =>
        {
            // Register eviction tracking for the newly created entry
            // This callback pattern ensures every cached item contributes to eviction metrics
            // regardless of how it was created (Set, GetOrCreate, CreateEntry)
            entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
            {
                var self = (MeteredMemoryCache)state!;
                if (!self._disposed)
                {
                    // Thread-safe tag creation for eviction reason dimensional metric
                    var tags = CreateEvictionTags(self._baseTags, reason.ToString());
                    self._evictions.Add(1, tags);
                }
            }, this);
            return factory(entry);
        });

        // Null safety check for reference types - helps catch factory bugs early
        if (created is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException("Factory returned null for a reference type; enable nullable annotations if null values are expected.");
        }
        return created!; // safe due to check above or value type
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Delegate to inner cache and emit appropriate metric based on result
        // This pattern ensures consistent hit/miss tracking across all access methods
        var hit = _inner.TryGetValue(key, out value);
        if (hit) _hits.Add(1, CreateBaseTags(_baseTags)); else _misses.Add(1, CreateBaseTags(_baseTags));
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = _inner.CreateEntry(key);

        // Pre-register eviction callback on the entry before returning to caller
        // This ensures eviction tracking regardless of how the caller configures the entry
        entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
        {
            var self = (MeteredMemoryCache)state!;
            if (!self._disposed)
            {
                var tags = CreateEvictionTags(self._baseTags, reason.ToString());
                self._evictions.Add(1, tags);
            }
        }, this);
        return entry;
    }

    /// <summary>
    /// Removes the cache entry associated with the specified key. If the entry exists, its eviction callback will automatically emit eviction metrics.
    /// </summary>
    /// <param name="key">The cache key to remove. Cannot be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this <see cref="MeteredMemoryCache"/> instance has been disposed.</exception>
    public void Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inner.Remove(key); // eviction callback (if any) will record eviction metric
    }

    /// <summary>
    /// Thread-safe helper to create base tags copy for hit/miss metric emissions.
    /// This method creates a new TagList instance to prevent defensive copy issues
    /// that could occur when the readonly _baseTags field is passed directly to Counter operations.
    /// </summary>
    private static TagList CreateBaseTags(TagList baseTags)
    {
        var tags = new TagList();

        // Copy base tags in a thread-safe manner by creating a new TagList
        // This approach prevents defensive copy mutation issues when readonly fields
        // are passed directly to Counter<T>.Add() operations
        // TagList enumeration is thread-safe for reading, but we create a new instance
        // to ensure consistent behavior across all metric emission patterns
        foreach (var tag in baseTags)
        {
            tags.Add(tag.Key, tag.Value);
        }

        return tags;
    }

    /// <summary>
    /// Thread-safe helper to create eviction tags by combining base tags with eviction reason.
    /// This method creates a new TagList instance to prevent concurrent modification exceptions
    /// that could occur when multiple threads simultaneously emit eviction metrics.
    /// </summary>
    private static TagList CreateEvictionTags(TagList baseTags, string reason)
    {
        var tags = new TagList();

        // Copy base tags in a thread-safe manner by creating a new TagList
        // This approach avoids sharing mutable state between eviction callbacks
        // which can be invoked concurrently from different threads (e.g., background eviction, manual removal)
        // TagList enumeration is thread-safe for reading, but we create a new instance
        // to ensure the eviction callback has its own independent tag collection
        foreach (var tag in baseTags)
        {
            tags.Add(tag.Key, tag.Value);
        }

        // Add the specific eviction reason as a dimensional tag
        // Common values: "Expired", "TokenExpired", "Capacity", "Removed", "Replaced"
        tags.Add("reason", reason);
        return tags;
    }

    /// <summary>
    /// Releases all resources used by this <see cref="MeteredMemoryCache"/> instance.
    /// Optionally disposes the inner <see cref="IMemoryCache"/> if <c>disposeInner</c> was set to <see langword="true"/> during construction.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_disposeInner)
            {
                _inner.Dispose();
            }
        }
    }
}
