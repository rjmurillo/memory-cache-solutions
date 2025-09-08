using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace CacheImplementations;

/// <summary>
/// Provides a single-flight wrapper over <see cref="IMemoryCache"/> so that only one concurrent caller
/// executes the value factory for a given key. All other concurrent callers for that key await the
/// completion of the first invocation and then read the populated cache entry, preventing duplicate work
/// and mitigating cache stampede / thundering herd effects.
/// </summary>
/// <remarks>
/// A lightweight <see cref="SemaphoreSlim"/> is created per key on demand and removed immediately after
/// the value has been produced and stored. This keeps the internal dictionary bounded by current in-flight
/// keys only. The cache entry is created with an absolute expiration relative to now specified by <paramref name="ttl"/>.
/// Additional entry configuration (size, priority, eviction tokens, etc.) can be supplied via <paramref name="configure"/>.
/// </remarks>
/// <param name="cache">Underlying memory cache used for storing produced values.</param>
public sealed class SingleFlightCache(IMemoryCache cache)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the cached value for <paramref name="key"/> if present; otherwise executes the asynchronous
    /// <paramref name="factory"/> exactly once (single-flight) among concurrent callers, caches its result
    /// with the specified <paramref name="ttl"/>, and returns the produced value.
    /// </summary>
    /// <typeparam name="T">The type of the value being cached.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="ttl">Absolute expiration duration relative to now for the created cache entry.</param>
    /// <param name="factory">Asynchronous factory used to create the value when it is missing.</param>
    /// <param name="configure">Optional callback to further configure the <see cref="ICacheEntry"/> before it is committed.</param>
    /// <param name="ct">Cancellation token observed while waiting for the per-key lock and during factory execution.</param>
    /// <returns>The cached or newly created value.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled while waiting on the lock or running the factory.</exception>
    /// <remarks>
    /// The cache hit path is allocation-free: if the value is present, it is returned directly with no new allocations.
    /// </remarks>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<Task<T>> factory,
        Action<ICacheEntry>? configure = null,
        CancellationToken ct = default)
    {
        if (cache.TryGetValue(key, out object? existing))
        {
            return (T)existing!; // value produced by factory is assumed non-null
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (cache.TryGetValue(key, out existing))
            {
                return (T)existing!;
            }

            var value = await factory().ConfigureAwait(false);
            using var entry = cache.CreateEntry(key);
            entry.AbsoluteExpirationRelativeToNow = ttl;
            configure?.Invoke(entry);
            entry.Value = value!;
            return value;
        }
        finally
        {
            gate.Release();
            _ = _locks.TryRemove(key, out _);
        }
    }
}