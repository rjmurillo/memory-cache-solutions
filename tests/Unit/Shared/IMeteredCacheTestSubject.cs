using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Unit.Shared;

/// <summary>
/// Defines a common interface for test subjects that wrap <see cref="IMemoryCache"/>
/// implementations with metric capabilities.
/// </summary>
public interface IMeteredCacheTestSubject : IDisposable
{
    /// <summary>
    /// Gets the underlying <see cref="IMemoryCache"/> instance being tested.
    /// </summary>
    IMemoryCache Cache { get; }

    /// <summary>
    /// Gets the <see cref="Meter"/> instance used by the cache for metric collection.
    /// </summary>
    Meter Meter { get; }

    /// <summary>
    /// Gets the logical name of the cache instance.
    /// </summary>
    string? CacheName { get; }

    /// <summary>
    /// Gets a value indicating whether metrics are enabled for this cache instance.
    /// </summary>
    bool MetricsEnabled { get; }

    /// <summary>
    /// Gets the implementation type name for test identification and debugging.
    /// </summary>
    string ImplementationType { get; }

    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    /// <returns>A <see cref="CacheImplementations.CacheStatistics"/> instance, or <see langword="null"/> if not supported.</returns>
    object? GetCurrentStatistics();

    /// <summary>
    /// Publishes accumulated metrics if supported by the implementation.
    /// For MeteredMemoryCache, this is a no-op since Observable instruments auto-poll.
    /// </summary>
    void PublishMetrics();
}
