using Microsoft.Extensions.Caching.Memory;

namespace CacheImplementations;

/// <summary>
/// Extensions providing a Stale-While-Revalidate (SWR) caching pattern over <see cref="IMemoryCache"/>.
/// </summary>
public static class SwrCacheExtensions
{
    private sealed class SwrBox<T>
    {
        public readonly T Value;
        public readonly DateTimeOffset FreshUntil;
        public int Refreshing; // 0 = no, 1 = yes
        public SwrBox(T value, DateTimeOffset freshUntil)
        {
            Value = value;
            FreshUntil = freshUntil;
        }
    }

    /// <summary>
    /// Gets a value from cache, serving stale content while triggering a single background refresh when the
    /// value becomes stale (fresh TTL elapsed but still inside stale window). After the stale window passes
    /// the entry is evicted and callers block on a new factory execution.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="cache">The memory cache.</param>
    /// <param name="key">Cache key.</param>
    /// <param name="opt">SWR options controlling freshness and stale windows.</param>
    /// <param name="factory">Asynchronous value factory. Only one foreground call executes it on a miss; only one background refresh runs while stale.</param>
    /// <param name="ct">Cancellation token for the foreground factory execution (miss path). Background refresh uses <see cref="CancellationToken.None"/> so it is not tied to the caller's token.</param>
    /// <returns>The fresh or stale value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="opt"/> has invalid TTL or Stale values (TTL must be > 0, Stale must be >= 0).</exception>
    public static async Task<T> GetOrCreateSwrAsync<T>(
        this IMemoryCache cache,
        string key,
        SwrOptions opt,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default)
    {
        if (opt.Ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(opt), "Ttl must be > 0.");
        if (opt.Stale < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(opt), "Stale must be >= 0.");

        var now = DateTimeOffset.UtcNow;

        if (cache.TryGetValue<SwrBox<T>>(key, out var box) && box is not null)
        {
            if (now <= box.FreshUntil)
            {
                return box.Value; // fresh
            }
            TryStartBackgroundRefresh(cache, key, opt, factory, box);
            return box.Value; // serve stale
        }

        var value = await factory(ct).ConfigureAwait(false);
        var freshUntil = now + opt.Ttl;
        var newBox = new SwrBox<T>(value, freshUntil);
        cache.Set(key, newBox, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = opt.Ttl + opt.Stale,
        });
        return value;
    }

    private static void TryStartBackgroundRefresh<T>(
        IMemoryCache cache,
        string key,
        SwrOptions opt,
        Func<CancellationToken, Task<T>> factory,
        SwrBox<T> box)
    {
        if (Interlocked.CompareExchange(ref box.Refreshing, 1, 0) != 0)
        {
            return; // already refreshing
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var newValue = await factory(CancellationToken.None).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                var newBox = new SwrBox<T>(newValue, now + opt.Ttl);
                cache.Set(key, newBox, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = opt.Ttl + opt.Stale,
                });
            }
            catch
            {
                // swallow errors; stale value will remain until eviction
            }
            finally
            {
                Volatile.Write(ref box.Refreshing, 0);
            }
        });
    }
}
