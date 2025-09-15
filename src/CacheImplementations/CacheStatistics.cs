namespace CacheImplementations;

/// <summary>
/// Represents cache statistics at a point in time.
/// </summary>
/// <remarks>
/// This class provides a snapshot of cache performance metrics including hit counts,
/// miss counts, eviction counts, and the calculated hit ratio.
/// </remarks>
public sealed class CacheStatistics
{
    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    /// <value>The number of times a requested item was found in the cache.</value>
    public long HitCount { get; init; }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    /// <value>The number of times a requested item was not found in the cache.</value>
    public long MissCount { get; init; }

    /// <summary>
    /// Gets the total number of evictions.
    /// </summary>
    /// <value>The number of items that have been removed from the cache due to expiration, size limits, or manual removal.</value>
    public long EvictionCount { get; init; }

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    /// <value>The current count of items stored in the cache.</value>
    public long CurrentEntryCount { get; init; }

    /// <summary>
    /// Gets the cache name.
    /// </summary>
    /// <value>The logical name of the cache instance, or <see langword="null"/> if no name was provided.</value>
    public string? CacheName { get; init; }

    /// <summary>
    /// Gets the hit ratio as a percentage.
    /// </summary>
    /// <value>
    /// A value between 0 and 100 representing the percentage of cache requests that resulted in hits.
    /// Returns 0 when no requests have been made.
    /// </value>
    /// <remarks>
    /// The hit ratio is calculated as: <c>(HitCount / (HitCount + MissCount)) * 100</c>.
    /// </remarks>
    public double HitRatio
    {
        get
        {
            var total = HitCount + MissCount;
            return total > 0 ? (double)HitCount / total * 100 : 0;
        }
    }
}
