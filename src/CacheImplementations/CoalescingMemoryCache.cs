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

    /// <summary>
    /// Initializes a new instance of <see cref="CoalescingMemoryCache"/> that decorates the specified <see cref="IMemoryCache"/> with single-flight coalescing behavior.
    /// </summary>
    /// <param name="inner">The <see cref="IMemoryCache"/> implementation to decorate with coalescing behavior. Cannot be <see langword="null"/>.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="inner"/> cache when this instance is disposed. Defaults to <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
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
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to retrieve or create. Cannot be <see langword="null"/>.</param>
    /// <param name="createAsync">The asynchronous factory function to create a new cache entry if the key is not found. Cannot be <see langword="null"/>.</param>
    /// <returns>The cached value if found, or the newly created value from the factory.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="createAsync"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Creates a new <see cref="ICacheEntry"/> for the specified key.
    /// </summary>
    /// <param name="key">The cache key for the entry.</param>
    /// <returns>A new <see cref="ICacheEntry"/> instance.</returns>
    public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);

    /// <summary>
    /// Disposes this cache instance and optionally the underlying cache.
    /// </summary>
    public void Dispose()
    {
        if (_disposeInner)
        {
            _inner.Dispose();
        }
    }

    /// <summary>
    /// Removes the cache entry with the specified key.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    public void Remove(object key) => _inner.Remove(key);

    /// <summary>
    /// Attempts to get the cached value for the specified key.
    /// </summary>
    /// <param name="key">The cache key to retrieve.</param>
    /// <param name="value">When this method returns, contains the cached value if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the key was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);
}