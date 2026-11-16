using Microsoft.Extensions.Caching.Memory;

namespace Benchmarks.Async;

/// <summary>
/// The result of an async cache lookup: the value and whether the cache already held it.
/// </summary>
/// <param name="Value">The cached or fetched value.</param>
/// <param name="IsHit"><see langword="true"/> if the cache held the key before the call; otherwise, <see langword="false"/>.</param>
public readonly record struct CacheLookup(string? Value, bool IsHit);

/// <summary>
/// An async facade over a synchronous <see cref="IMemoryCache"/>, shaped like the
/// async cache services ASP.NET Core consumers write over this library.
/// A cache hit completes synchronously, so the cost under test is the async
/// state machine, not the cache lookup.
/// </summary>
/// <param name="cache">The synchronous cache to wrap.</param>
public sealed class AsyncCacheAdapter(IMemoryCache cache)
{
    /// <summary>
    /// Gets the value for <paramref name="key"/>, fetching and storing it on a miss.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token to cancel the fetch on a miss.</param>
    /// <returns>The cached or fetched value, and whether the lookup was a hit.</returns>
    public async ValueTask<CacheLookup> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(key, out string? value))
        {
            return new CacheLookup(value, IsHit: true);
        }

        string fetched = await FetchAsync(key, cancellationToken).ConfigureAwait(false);
        cache.Set(key, fetched);
        return new CacheLookup(fetched, IsHit: false);
    }

    private static ValueTask<string> FetchAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult("value-for-" + key);
    }
}

/// <summary>
/// A second async layer over an <see cref="AsyncCacheAdapter"/> that counts hits,
/// mirroring a decorator stack: consumer service over metrics layer over cache.
/// Every hit awaits through both layers and completes synchronously.
/// </summary>
/// <param name="inner">The inner async cache layer.</param>
public sealed class LayeredAsyncCacheAdapter(AsyncCacheAdapter inner)
{
    private long _hits;

    /// <summary>
    /// Gets the number of lookups that found the key already cached.
    /// </summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>
    /// Gets the value for <paramref name="key"/> through the inner layer, counting a hit
    /// only when the inner layer reports the key was already cached.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token to cancel the fetch on a miss.</param>
    /// <returns>The cached or fetched value, and whether the lookup was a hit.</returns>
    public async ValueTask<CacheLookup> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        CacheLookup lookup = await inner.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (lookup.IsHit)
        {
            Interlocked.Increment(ref _hits);
        }

        return lookup;
    }
}
