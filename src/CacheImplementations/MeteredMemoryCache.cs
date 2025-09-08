using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

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

    public MeteredMemoryCache(IMemoryCache inner, Meter meter, string? cacheName = null, bool disposeInner = false)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(meter);
        _inner = inner;
        _disposeInner = disposeInner;
        _hits = meter.CreateCounter<long>("cache_hits_total");
        _misses = meter.CreateCounter<long>("cache_misses_total");
        _evictions = meter.CreateCounter<long>("cache_evictions_total");
        _tags = new TagList();
        if (!string.IsNullOrEmpty(cacheName))
        {
            _tags.Add("cache.name", cacheName);
        }
    }

    // No backward compatibility constructor needed; use named parameters for clarity.

    /// <summary>
    /// Strongly typed convenience method that records hit/miss metrics.
    /// </summary>
    public bool TryGet<T>(object key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
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
        options ??= new MemoryCacheEntryOptions();
        options.RegisterPostEvictionCallback(static (_, _, reason, state) =>
        {
            var self = (MeteredMemoryCache)state!;
            var tags = self._tags;
            tags.Add("reason", reason.ToString());
            self._evictions.Add(1, tags);
            tags.RemoveAt(tags.Count - 1);
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

        if (_inner.TryGetValue(key, out var existing) && existing is T hit)
        {
            _hits.Add(1, _tags);
            return hit;
        }

        _misses.Add(1, _tags);
        var created = _inner.GetOrCreate(key, entry =>
        {
            entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
            {
                var self = (MeteredMemoryCache)state!;
                var tags = self._tags;
            tags.Add("reason", reason.ToString());
                self._evictions.Add(1, tags);
                tags.RemoveAt(tags.Count - 1);
            }, this);
            return factory(entry);
        });

        if (created is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException("Factory returned null for a reference type; enable nullable annotations if null values are expected.");
        }
        return created!; // safe due to check above or value type
    }

    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hit = _inner.TryGetValue(key, out value);
        if (hit) _hits.Add(1, _tags); else _misses.Add(1, _tags);
        return hit;
    }

    public ICacheEntry CreateEntry(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var entry = _inner.CreateEntry(key);
        entry.RegisterPostEvictionCallback(static (_, _, reason, state) =>
        {
            var self = (MeteredMemoryCache)state!;
            var tags = self._tags;
            tags.Add("reason", reason.ToString());
            self._evictions.Add(1, tags);
            tags.RemoveAt(tags.Count - 1);
        }, this);
        return entry;
    }

    public void Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _inner.Remove(key); // eviction callback (if any) will record eviction metric
    }

    public void Dispose()
    {
        if (_disposeInner)
        {
            _inner.Dispose();
        }
    }
}
