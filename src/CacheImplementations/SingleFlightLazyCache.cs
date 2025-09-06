using System;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;

namespace CacheImplementations;

/// <summary>
/// A single-flight cache implementation that stores the produced <see cref="Task{TResult}"/> directly
/// inside the underlying <see cref="IMemoryCache"/>. Concurrent callers for the same key receive the
/// same in-flight <see cref="Task"/> ensuring the value factory executes only once.
/// </summary>
/// <remarks>
/// Previous version used <c>Lazy&lt;Task&lt;T&gt;&gt;</c>. Removing the extra <c>Lazy</c> wrapper saves one object
/// allocation per miss while still relying on the atomic nature of <see cref="IMemoryCache.GetOrCreate"/>
/// to guarantee a single invocation of the value factory.
/// </remarks>
public sealed class SingleFlightLazyCache(IMemoryCache cache)
{
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <summary>
    /// Gets (or creates) a cached value. If the value for <paramref name="key"/> is missing a new
    /// <see cref="Task{TResult}"/> representing the asynchronous factory is created and stored, ensuring only one
    /// execution of <paramref name="factory"/> even under high concurrency.
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
            // Fast paths for existing entry
            if (boxed is Task<T> existingTask)
            {
                if (existingTask.IsCompletedSuccessfully)
                {
                    // Avoid the async state machine when already completed successfully.
                    return existingTask.Result;
                }
                return await existingTask.WaitAsync(ct).ConfigureAwait(false);
            }
            if (boxed is T directHit)
            {
                return directHit;
            }
        }

        // Create (or retrieve if another thread wins the race) the in-flight task.
        var task = _cache.GetOrCreate<Task<T>>(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            configure?.Invoke(entry);
            return factory();
        })!;

        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous variant to avoid creating an async state machine when the factory is naturally synchronous.
    /// It still cooperates with concurrent asynchronous callers for the same key (they will see the materialized
    /// value) and vice versa.
    /// </summary>
    public T GetOrCreate<T>(
        string key,
        TimeSpan ttl,
        Func<T> factory,
        Action<ICacheEntry>? configure = null,
        CancellationToken ct = default)
    {
        _ = ct; // no waiting occurs in this path; parameter retained for signature parity
        if (_cache.TryGetValue(key, out object? boxed))
        {
            if (boxed is Task<T> existingTask)
            {
                if (existingTask.IsCompletedSuccessfully)
                {
                    return existingTask.Result;
                }
                return existingTask.GetAwaiter().GetResult();
            }
            if (boxed is T directHit)
            {
                return directHit;
            }
        }

        // Create the value (single-flight ensured by IMemoryCache internal locking around GetOrCreate)
        var value = _cache.GetOrCreate<T>(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            configure?.Invoke(entry);
            return factory();
        });

        return value!;
    }
}
