using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System.Linq;

namespace CacheImplementations;

/// <summary>
/// IMemoryCache decorator that emits OpenTelemetry / .NET metrics for cache hits, misses and evictions.
/// Instruments:
///  - cache_hits_total (Counter<long>)
///  - cache_misses_total (Counter<long>)
///  - cache_evictions_total (Counter<long>) with tag "reason" = <see cref="PostEvictionReason"/> string
/// </summary>
public sealed class MeteredMemoryCache : IMemoryCache
{
    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _evictions;
    private readonly TagList _tags;
    private bool _disposed;

    /// <summary>
    /// Gets the logical name of this cache instance, if provided.
    /// </summary>
    public string? Name { get; }

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

        // Initialize TagList for dimensional metrics - this will be shared across all metric emissions
        // TagList is used for high-performance metric tagging with minimal allocations
        _tags = new TagList();
        if (!string.IsNullOrEmpty(cacheName))
        {
            // Add cache.name as a dimensional tag to distinguish metrics from multiple cache instances
            // This enables filtering and aggregation by cache name in monitoring dashboards
            _tags.Add("cache.name", cacheName);
            Name = cacheName;
        }
    }

    /// <summary>
    /// Options-based constructor for advanced configuration.
    /// </summary>
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
        _tags = new TagList();
        if (!string.IsNullOrEmpty(options.CacheName))
        {
            _tags.Add("cache.name", options.CacheName);
            Name = options.CacheName;
        }

        // Add user-defined custom tags while preventing cache.name override
        // This filtering ensures cache.name remains consistent if set via CacheName property
        foreach (var kvp in options.AdditionalTags.Where(kvp => !string.Equals(kvp.Key, "cache.name", System.StringComparison.Ordinal)))
        {
            _tags.Add(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Strongly typed convenience method that records hit/miss metrics.
    /// </summary>
    public bool TryGet<T>(object key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inner.TryGetValue(key, out var obj) && obj is T t)
        {
            _hits.Add(1, _tags);
            value = t;
            return true;
        }
        _misses.Add(1, _tags);
        value = default!;
        return false;
    }

    /// <summary>
    /// Set value and register eviction callback to emit eviction metric.
    /// </summary>
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
                var tags = CreateEvictionTags(self._tags, reason.ToString());
                self._evictions.Add(1, tags);
            }
        }, this);
        _inner.Set(key, value, options);
    }

    /// <summary>
    /// Get or create value while emitting hit/miss metrics and registering eviction callback.
    /// </summary>
    public T GetOrCreate<T>(object key, Func<ICacheEntry, T> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // First attempt: check if key exists and value is correct type (cache hit scenario)
        if (_inner.TryGetValue(key, out var existing) && existing is T hit)
        {
            _hits.Add(1, _tags);
            return hit;
        }

        // Cache miss: record miss metric and delegate to inner cache for creation
        // This ensures accurate hit/miss ratios even with concurrent access patterns
        _misses.Add(1, _tags);
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
                    var tags = CreateEvictionTags(self._tags, reason.ToString());
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

    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Delegate to inner cache and emit appropriate metric based on result
        // This pattern ensures consistent hit/miss tracking across all access methods
        var hit = _inner.TryGetValue(key, out value);
        if (hit) _hits.Add(1, _tags); else _misses.Add(1, _tags);
        return hit;
    }

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
                var tags = CreateEvictionTags(self._tags, reason.ToString());
                self._evictions.Add(1, tags);
            }
        }, this);
        return entry;
    }

    public void Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inner.Remove(key); // eviction callback (if any) will record eviction metric
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
