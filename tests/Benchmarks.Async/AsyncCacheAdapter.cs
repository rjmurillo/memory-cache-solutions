using Microsoft.Extensions.Caching.Memory;

namespace Benchmarks.Async;

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
    /// <returns>The cached or fetched value.</returns>
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(key, out string? value))
        {
            return value;
        }

        string fetched = await FetchAsync(key, cancellationToken).ConfigureAwait(false);
        cache.Set(key, fetched);
        return fetched;
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
    /// Gets the number of hits observed by this layer.
    /// </summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>
    /// Gets the value for <paramref name="key"/> through the inner layer and counts a hit.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token to cancel the fetch on a miss.</param>
    /// <returns>The cached or fetched value.</returns>
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        string? value = await inner.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (value is not null)
        {
            Interlocked.Increment(ref _hits);
        }

        return value;
    }
}
