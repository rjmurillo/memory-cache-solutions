using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Unit.Shared;

/// <summary>
/// Defines a common interface for test subjects that wrap <see cref="IMemoryCache"/>
/// implementations with metric capabilities. This allows for writing generic tests
/// that can be run against both MeteredMemoryCache and OptimizedMeteredMemoryCache.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables the DRY (Don't Repeat Yourself) principle in testing by
/// providing a standardized way to test different cache implementations. Test scenarios
/// can be written once in the <see cref="MeteredCacheTestBase{T}"/> and executed
/// against multiple implementations.
/// </para>
/// <para>
/// The interface abstracts away implementation-specific details while providing access
/// to common functionality like metric collection, statistics retrieval, and cache
/// operations. This is particularly useful for AI agents and developers who need to
/// understand the testing patterns and capabilities of different cache implementations.
/// </para>
/// </remarks>
public interface IMeteredCacheTestSubject : IDisposable
{
    /// <summary>
    /// Gets the underlying <see cref="IMemoryCache"/> instance being tested.
    /// </summary>
    /// <value>The cache instance wrapped by this test subject.</value>
    IMemoryCache Cache { get; }

    /// <summary>
    /// Gets the <see cref="Meter"/> instance used by the cache for metric collection.
    /// </summary>
    /// <value>The meter instance, either provided during construction or created internally.</value>
    Meter Meter { get; }

    /// <summary>
    /// Gets the logical name of the cache instance.
    /// </summary>
    /// <value>The cache name, or <see langword="null"/> if no name was provided.</value>
    string? CacheName { get; }

    /// <summary>
    /// Gets a value indicating whether metrics are enabled for this cache instance.
    /// </summary>
    /// <value><see langword="true"/> if metrics are enabled; otherwise, <see langword="false"/>.</value>
    /// <remarks>
    /// This property helps tests determine whether to expect metric emission.
    /// Some implementations may support disabling metrics for performance testing.
    /// </remarks>
    bool MetricsEnabled { get; }

    /// <summary>
    /// Gets the implementation type name for test identification and debugging.
    /// </summary>
    /// <value>A string identifying the specific cache implementation being tested.</value>
    /// <remarks>
    /// This property is useful for test output and debugging, allowing tests to
    /// identify which implementation is being tested in shared test scenarios.
    /// </remarks>
    string ImplementationType { get; }

    /// <summary>
    /// Gets current cache statistics if supported by the implementation.
    /// </summary>
    /// <returns>
    /// A statistics object containing current cache metrics, or <see langword="null"/>
    /// if the implementation doesn't support statistics retrieval.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method provides access to implementation-specific statistics that may
    /// not be available through the standard <see cref="IMemoryCache"/> interface.
    /// For example, OptimizedMeteredMemoryCache returns detailed
    /// statistics including hit counts, miss counts, and hit ratios.
    /// </para>
    /// <para>
    /// The return type is <see cref="object"/> to accommodate different statistics
    /// types from different implementations. Callers should check the type and
    /// cast appropriately.
    /// </para>
    /// </remarks>
    object? GetCurrentStatistics();

    /// <summary>
    /// Publishes accumulated metrics if supported by the implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is used to trigger metric publishing for implementations that
    /// use batched or periodic metric emission. For implementations that publish
    /// metrics immediately (like MeteredMemoryCache), this method
    /// may do nothing.
    /// </para>
    /// <para>
    /// This is particularly useful for testing implementations like
    /// OptimizedMeteredMemoryCache that accumulate metrics using
    /// atomic operations and publish them periodically to reduce overhead.
    /// </para>
    /// </remarks>
    void PublishMetrics();
}
