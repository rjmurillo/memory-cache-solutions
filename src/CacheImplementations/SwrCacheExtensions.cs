using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

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
        public SwrBox(T value, DateTimeOffset freshUntil)
        {
            Value = value;
            FreshUntil = freshUntil;
        }
    }

    private sealed class SwrBoxWithRefresh<T>
    {
        public readonly SwrBox<T> Box;
        public SwrBoxWithRefresh(SwrBox<T> box)
        {
            Box = box;
        }
    }

    // Global tracking of ongoing refreshes to prevent duplicate background refreshes
    private static readonly ConcurrentDictionary<string, Task> _ongoingRefreshes = new(StringComparer.Ordinal);

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

        if (cache.TryGetValue<SwrBoxWithRefresh<T>>(key, out var boxWithRefresh) && boxWithRefresh is not null)
        {
            var box = boxWithRefresh.Box;
            if (now <= box.FreshUntil)
            {
                return box.Value; // fresh
            }
            
            // Value is stale, try to start background refresh
            _ = TryStartBackgroundRefresh(cache, key, opt, factory);
            return box.Value; // serve stale
        }

        var value = await factory(ct).ConfigureAwait(false);
        var freshUntil = now + opt.Ttl;
        var newBox = new SwrBox<T>(value, freshUntil);
        var newBoxWithRefresh = new SwrBoxWithRefresh<T>(newBox);
        cache.Set(key, newBoxWithRefresh, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = opt.Ttl + opt.Stale,
        });
        return value;
    }

    private static Task<T>? TryStartBackgroundRefresh<T>(
        IMemoryCache cache,
        string key,
        SwrOptions opt,
        Func<CancellationToken, Task<T>> factory)
    {
        // Use global tracking to prevent duplicate background refreshes
        var refreshTask = _ongoingRefreshes.GetOrAdd(key, k => Task.Run(async () =>
        {
            try
            {
                var newValue = await factory(CancellationToken.None).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                var newBox = new SwrBox<T>(newValue, now + opt.Ttl);
                var newBoxWithRefresh = new SwrBoxWithRefresh<T>(newBox);

                // Update the cache entry atomically
                cache.Set(k, newBoxWithRefresh, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = opt.Ttl + opt.Stale,
                });
                
                return newValue;
            }
            catch
            {
                // swallow errors; stale value will remain until eviction
                return default(T)!;
            }
            finally
            {
                // Remove from ongoing refreshes when done
                _ongoingRefreshes.TryRemove(k, out Task? _);
            }
        }));

        // Return the task if we just created it, null if it was already running
        return (Task<T>?)refreshTask;
    }
}
