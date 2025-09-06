using System;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;

namespace CacheImplementations;

/// <summary>
/// A single-flight cache implementation that stores a <see cref="Lazy{T}"/> of <see cref="Task{TResult}"/>
/// inside the underlying <see cref="IMemoryCache"/>. Concurrent callers for the same key receive the
/// same in-flight <see cref="Task"/> ensuring the value factory executes only once.
/// </summary>
/// <remarks>
/// Compared to <see cref="SingleFlightCache"/> this version does not employ an external per-key lock.
/// Instead, it relies on the atomic nature of <see cref="IMemoryCache.GetOrCreate"/> and the
/// publication guarantees of <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>.
/// This keeps contention minimal while still preventing duplicate work.
/// </remarks>
public sealed class SingleFlightLazyCache(IMemoryCache cache)
{
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <summary>
    /// Gets (or creates) a cached value using an asynchronous factory. Stores the <see cref="Task{TResult}"/>
    /// directly in the underlying <see cref="IMemoryCache"/> removing one <see cref="Lazy{T}"/> allocation.
    /// Concurrent callers rely on the internal lock taken by <see cref="IMemoryCache.GetOrCreate"/> ensuring
    /// the factory executes only once per miss.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<Task<T>> factory,
        Action<ICacheEntry>? configure = null,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out object? boxed))
        {
            switch (boxed)
            {
                case Task<T> existingTask:
                    if (existingTask.IsCompletedSuccessfully)
                    {
                        // Fast path: synchronous result without async state machine
                        return existingTask.GetAwaiter().GetResult();
                    }
                    return await existingTask.WaitAsync(ct).ConfigureAwait(false);
                case T directValue:
                    return directValue; // value stored directly (e.g., via sync overload)
                case Lazy<Task<T>> legacyLazy: // backward compatibility if previously cached by old version
                    var taskFromLazy = legacyLazy.Value;
                    if (taskFromLazy.IsCompletedSuccessfully)
                    {
                        return taskFromLazy.GetAwaiter().GetResult();
                    }
                    return await taskFromLazy.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        // Miss: create or retrieve single-flight task entry
        var task = _cache.GetOrCreate<Task<T>>(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            configure?.Invoke(entry);
            return factory();
        })!;

        if (task.IsCompletedSuccessfully)
        {
            return task.GetAwaiter().GetResult();
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous factory overload avoiding Task machinery when the value can be produced synchronously.
    /// The produced value is stored directly (not wrapped in a <see cref="Task"/>) and returned to all
    /// concurrent callers using the single-flight nature of <see cref="IMemoryCache.GetOrCreate"/>.
    /// </summary>
    public T GetOrCreate<T>(
        string key,
        TimeSpan ttl,
        Func<T> factory,
        Action<ICacheEntry>? configure = null)
    {
        if (_cache.TryGetValue(key, out object? boxed) && boxed is T existing)
        {
            return existing;
        }

        return _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            configure?.Invoke(entry);
            return factory();
        })!;
    }
}
