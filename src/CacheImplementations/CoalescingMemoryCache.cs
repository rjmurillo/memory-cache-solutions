using Microsoft.Extensions.Caching.Memory;

using System.Collections.Concurrent;

namespace CacheImplementations;

/// <summary>
/// Decorator over <see cref="IMemoryCache"/> that coalesces concurrent cold-miss value creation so the
/// asynchronous factory is executed only once per key (single-flight). Implements <see cref="IMemoryCache"/>
/// for drop-in substitution anywhere an IMemoryCache is used.
/// </summary>
public sealed class CoalescingMemoryCache : IMemoryCache
{
    // Tracks in-flight creations per key. Value is a Lazy<Task<object>> boxed as object.
    private readonly ConcurrentDictionary<object, object> _inflight = new();

    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;

    public CoalescingMemoryCache(IMemoryCache inner, bool disposeInner = false)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _disposeInner = disposeInner;
    }

    /// <summary>
    /// Gets the cached value for <paramref name="key"/> if present; otherwise executes the asynchronous
    /// <paramref name="createAsync"/> exactly once for all concurrent callers, caches its result, and returns it.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(object key, Func<ICacheEntry, Task<T>> createAsync)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(createAsync);

        // Fast path
        if (_inner.TryGetValue(key, out var existing) && existing is T existingT)
        {
            return existingT;
        }

        // Miss path: obtain (or create) the single-flight lazy
        var lazyObj = _inflight.GetOrAdd(key, static (k, s) => s.self.CreateLazyTask<T>(k, s.factory), (self: this, factory: createAsync));
        var lazy = (Lazy<Task<T>>)lazyObj;

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<object, object>(key, lazyObj));
        }
    }

    private Lazy<Task<T>> CreateLazyTask<T>(object key, Func<ICacheEntry, Task<T>> factory) =>
        new(() => ExecuteAndCache(key, factory), LazyThreadSafetyMode.ExecutionAndPublication);

    private async Task<T> ExecuteAndCache<T>(object key, Func<ICacheEntry, Task<T>> factory)
    {
        // Double-check under flight
        if (_inner.TryGetValue(key, out var hit) && hit is T hitT)
        {
            return hitT;
        }

        using var entry = _inner.CreateEntry(key);
        var value = await factory(entry).ConfigureAwait(false);
        entry.Value = value!; // commit
        return value;
    }

    // IMemoryCache passthrough members
    public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);

    public void Dispose()
    {
        if (_disposeInner)
        {
            _inner.Dispose();
        }
    }

    public void Remove(object key) => _inner.Remove(key);

    public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);
}