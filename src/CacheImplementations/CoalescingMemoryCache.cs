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
    // Tracks in-flight creations per key. Value typed as Lazy<Task<object?>> to avoid repeated casting
    // from object -> Lazy<Task<T>> per call site. We erase T only inside this dictionary.
    private readonly ConcurrentDictionary<object, Lazy<Task<object?>>> _inflight = new();

    private readonly IMemoryCache _inner;
    private readonly bool _disposeInner;
    private readonly bool _skipSecondCacheCheck; // optional micro-opt to allow occasional duplicate work

    public CoalescingMemoryCache(IMemoryCache inner, bool disposeInner = false, bool skipSecondCacheCheck = false)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _disposeInner = disposeInner;
        _skipSecondCacheCheck = skipSecondCacheCheck;
    }

    /// <summary>
    /// Gets the cached value for <paramref name="key"/> if present; otherwise executes the asynchronous
    /// <paramref name="createAsync"/> exactly once for all concurrent callers, caches its result, and returns it.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(object key, Func<ICacheEntry, Task<T>> createAsync)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(createAsync);

        // Fast path hit
        if (_inner.TryGetValue(key, out var existing) && existing is T existingT)
        {
            return existingT;
        }

        var lazy = _inflight.GetOrAdd(key, k => CreateLazy(k, WrapFactory(createAsync)));

        object? resultObj;
        try
        {
            var task = lazy.Value;
            if (task.IsCompletedSuccessfully)
            {
                resultObj = task.Result;
            }
            else
            {
                resultObj = await task.ConfigureAwait(false);
            }
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<object, Lazy<Task<object?>>>(key, lazy));
        }

        return (T)resultObj!;
    }

    /// <summary>
    /// ValueTask factory overload. Allows callers to avoid an allocation when completion is synchronous.
    /// </summary>
    public async ValueTask<T> GetOrCreateAsync<T>(object key, Func<ICacheEntry, ValueTask<T>> createAsync)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(createAsync);

        if (_inner.TryGetValue(key, out var existing) && existing is T existingT)
        {
            return existingT;
        }

        var lazy = _inflight.GetOrAdd(key, k => CreateLazy(k, WrapFactory(createAsync)));

        object? resultObj;
        try
        {
            var task = lazy.Value;
            if (task.IsCompletedSuccessfully)
            {
                resultObj = task.Result;
            }
            else
            {
                resultObj = await task.ConfigureAwait(false);
            }
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<object, Lazy<Task<object?>>>(key, lazy));
        }

        return (T)resultObj!;
    }

    /// <summary>
    /// Synchronous factory path to avoid async state machine when the value can be produced immediately.
    /// </summary>
    public T GetOrCreate<T>(object key, Func<ICacheEntry, T> create)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(create);

        if (_inner.TryGetValue(key, out var existing) && existing is T existingT)
        {
            return existingT;
        }

        var lazy = _inflight.GetOrAdd(key, k => CreateLazy(k, WrapFactory(create)));

        object? resultObj;
        try
        {
            var task = lazy.Value;
            if (task.IsCompletedSuccessfully)
            {
                resultObj = task.Result;
            }
            else
            {
                // synchronous wait acceptable here
                resultObj = task.GetAwaiter().GetResult();
            }
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<object, Lazy<Task<object?>>>(key, lazy));
        }

        return (T)resultObj!;
    }

    /// <summary>
    /// Variant that reports whether this invocation executed the factory (won the race) and supports
    /// cancellation that only applies while awaiting the in-flight task (not the underlying execution).
    /// </summary>
    public async Task<(bool created, T value)> TryGetOrCreateAsync<T>(object key, Func<ICacheEntry, Task<T>> createAsync, CancellationToken waitCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(createAsync);

        if (_inner.TryGetValue(key, out var existing) && existing is T existingT)
        {
            return (false, existingT);
        }

        var newLazy = CreateLazy(key, WrapFactory(createAsync));
        var lazy = _inflight.GetOrAdd(key, newLazy);
        var created = ReferenceEquals(newLazy, lazy);

        try
        {
            var task = lazy.Value;
            object? obj;
            if (task.IsCompletedSuccessfully)
            {
                obj = task.Result;
            }
            else
            {
                obj = await task.WaitAsync(waitCancellationToken).ConfigureAwait(false);
            }
            return (created, (T)obj!);
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<object, Lazy<Task<object?>>>(key, lazy));
        }
    }

    private Lazy<Task<object?>> CreateLazy(object key, Func<ICacheEntry, Task<object?>> factory) =>
        new(() => ExecuteAndCache(key, factory), LazyThreadSafetyMode.ExecutionAndPublication);

    private async Task<object?> ExecuteAndCache(object key, Func<ICacheEntry, Task<object?>> factory)
    {
        if (!_skipSecondCacheCheck && _inner.TryGetValue(key, out var hit))
        {
            return hit; // another thread populated while we queued
        }

        using var entry = _inner.CreateEntry(key);
        var value = await factory(entry).ConfigureAwait(false);
        entry.Value = value; // commit
        return value;
    }

    // Factory wrappers
    private static Func<ICacheEntry, Task<object?>> WrapFactory<T>(Func<ICacheEntry, Task<T>> factory) => entry =>
    {
        var t = factory(entry);
        if (t.IsCompletedSuccessfully)
        {
            return Task.FromResult<object?>(t.Result!);
        }
        return Awaited(t);

        static async Task<object?> Awaited(Task<T> inner) => await inner.ConfigureAwait(false);
    };

    private static Func<ICacheEntry, Task<object?>> WrapFactory<T>(Func<ICacheEntry, ValueTask<T>> factory) => entry =>
    {
        var vt = factory(entry);
        if (vt.IsCompletedSuccessfully)
        {
            return Task.FromResult<object?>(vt.Result!);
        }
        return Awaited(vt);

        static async Task<object?> Awaited(ValueTask<T> inner) => await inner.ConfigureAwait(false);
    };

    private static Func<ICacheEntry, Task<object?>> WrapFactory<T>(Func<ICacheEntry, T> factory) => entry =>
    {
        var result = factory(entry);
        return Task.FromResult<object?>(result!);
    };

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