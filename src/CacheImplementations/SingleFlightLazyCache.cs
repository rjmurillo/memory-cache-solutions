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
    /// Gets (or creates) a cached value. If the value for <paramref name="key"/> is missing a new
    /// <see cref="Lazy{T}"/> representing the asynchronous factory is created and stored, ensuring only one
    /// execution of <paramref name="factory"/> even under high concurrency.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="key">Cache key.</param>
    /// <param name="ttl">Absolute expiration relative to now.</param>
    /// <param name="factory">Asynchronous factory used to create the value when missing.</param>
    /// <param name="configure">Optional cache entry configuration callback.</param>
    /// <param name="ct">Cancellation token (applies to awaiting the task, not to the underlying task once started).</param>
    /// <returns>The cached (or newly produced) value.</returns>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<Task<T>> factory,
        Action<ICacheEntry>? configure = null,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out object? boxed) && boxed is Lazy<Task<T>> existingLazy)
        {
            var existingTask = existingLazy.Value;
            if (existingTask.IsCompletedSuccessfully)
            {
                return existingTask.Result;
            }
            return await existingTask.WaitAsync(ct).ConfigureAwait(false);
        }
        else if (boxed is T directHit)
        {
            // A previously cached concrete value (not via this decorator). Return it directly.
            return directHit;
        }

        var lazy = _cache.GetOrCreate<Lazy<Task<T>>>(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            configure?.Invoke(entry);
            return new Lazy<Task<T>>(() => factory(), LazyThreadSafetyMode.ExecutionAndPublication);
        });

        var task = lazy!.Value;
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
}
