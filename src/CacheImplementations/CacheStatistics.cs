namespace CacheImplementations;

/// <summary>
/// Represents cache statistics at a point in time, aligned with <see cref="Microsoft.Extensions.Caching.Memory.MemoryCacheStatistics"/>.
/// </summary>
/// <remarks>
/// This class provides a snapshot of cache performance metrics including hit counts,
/// miss counts, eviction counts, and the calculated hit ratio. Property names align
/// with the BCL <c>MemoryCacheStatistics</c> type per dotnet/runtime#124140.
/// </remarks>
public sealed class CacheStatistics
{
    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    /// <value>The number of times a requested item was found in the cache.</value>
    public long TotalHits { get; init; }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    /// <value>The number of times a requested item was not found in the cache.</value>
    public long TotalMisses { get; init; }

    /// <summary>
    /// Gets the total number of automatic evictions (excludes explicit user removals).
    /// </summary>
    /// <value>The number of items removed from the cache due to expiration, size limits, or memory pressure.</value>
    public long TotalEvictions { get; init; }

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    /// <value>The current count of items stored in the cache.</value>
    public long CurrentEntryCount { get; init; }

    /// <summary>
    /// Gets the estimated size of all entries in the cache, if size tracking is enabled.
    /// </summary>
    /// <value>The estimated total size, or <see langword="null"/> if size tracking is not enabled.</value>
    public long? EstimatedSize { get; init; }

    /// <summary>
    /// Gets the hit ratio as a percentage.
    /// </summary>
    /// <value>
    /// A value between 0 and 100 representing the percentage of cache requests that resulted in hits.
    /// Returns 0 when no requests have been made.
    /// </value>
    /// <remarks>
    /// The hit ratio is calculated as: <c>(TotalHits / (TotalHits + TotalMisses)) * 100</c>.
    /// This is an extension beyond the BCL <c>MemoryCacheStatistics</c> type for convenience.
    /// </remarks>
    public double HitRatio
    {
        get
        {
            var total = TotalHits + TotalMisses;
            return total > 0 ? (double)TotalHits / total * 100 : 0;
        }
    }
}
