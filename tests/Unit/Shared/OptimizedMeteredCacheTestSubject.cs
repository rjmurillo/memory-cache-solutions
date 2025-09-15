using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

namespace Unit.Shared;

/// <summary>
/// Test subject wrapper for the <see cref="OptimizedMeteredMemoryCache"/> implementation.
/// Provides a standardized interface for testing the optimized cache implementation
/// with atomic operations and periodic metric publishing capabilities.
/// </summary>
/// <remarks>
/// This test subject enables shared test scenarios to be run against the optimized
/// cache implementation, providing access to its unique features like
/// <see cref="GetCurrentStatistics()"/> and <see cref="PublishMetrics()"/>.
/// </remarks>
public sealed class OptimizedMeteredCacheTestSubject : IMeteredCacheTestSubject
{
    private readonly OptimizedMeteredMemoryCache _cache;
    private readonly IMemoryCache _innerCache;
    private readonly bool _disposeInner;
    private readonly bool _disposeMeter;

    /// <summary>
    /// Gets the underlying <see cref="IMemoryCache"/> instance being tested.
    /// </summary>
    /// <value>The <see cref="OptimizedMeteredMemoryCache"/> instance.</value>
    public IMemoryCache Cache => _cache;

    /// <summary>
    /// Gets the <see cref="Meter"/> instance used by the cache for metric collection.
    /// </summary>
    /// <value>The meter instance, either provided during construction or created internally.</value>
    public Meter Meter { get; }

    /// <summary>
    /// Gets the logical name of the cache instance.
    /// </summary>
    /// <value>The cache name, or <see langword="null"/> if no name was provided.</value>
    public string? CacheName => _cache.Name;

    /// <summary>
    /// Gets a value indicating whether metrics are enabled for this cache instance.
    /// </summary>
    /// <value><see langword="true"/> if metrics are enabled; otherwise, <see langword="false"/>.</value>
    public bool MetricsEnabled { get; }

    /// <summary>
    /// Gets the implementation type name for test identification.
    /// </summary>
    /// <value>Always returns "OptimizedMeteredMemoryCache".</value>
    public string ImplementationType => "OptimizedMeteredMemoryCache";

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizedMeteredCacheTestSubject"/> class.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="cacheName">Optional logical name for the cache instance.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed.</param>
    /// <param name="enableMetrics">Whether to enable metric collection for the cache.</param>
    public OptimizedMeteredCacheTestSubject(
        IMemoryCache? innerCache = null,
        Meter? meter = null,
        string? cacheName = null,
        bool disposeInner = true,
        bool enableMetrics = true)
    {
        _innerCache = innerCache ?? new MemoryCache(new MemoryCacheOptions());
        _disposeInner = disposeInner;
        _disposeMeter = meter == null; // Only dispose meter if we created it
        Meter = meter ?? new Meter($"test.optimized.{Guid.NewGuid()}");
        MetricsEnabled = enableMetrics;
        _cache = new OptimizedMeteredMemoryCache(_innerCache, Meter, cacheName, disposeInner, enableMetrics);
    }

    /// <summary>
    /// Gets the current cache statistics if supported by the implementation.
    /// </summary>
    /// <returns>A <see cref="CacheStatistics"/> object containing current cache metrics, or <see langword="null"/> if not supported.</returns>
    /// <remarks>
    /// For <see cref="OptimizedMeteredMemoryCache"/>, this returns detailed statistics including
    /// hit count, miss count, eviction count, current entry count, and calculated hit ratio.
    /// </remarks>
    public object? GetCurrentStatistics()
    {
        return _cache.GetCurrentStatistics();
    }

    /// <summary>
    /// Publishes accumulated metrics if supported by the implementation.
    /// </summary>
    /// <remarks>
    /// For <see cref="OptimizedMeteredMemoryCache"/>, this publishes any accumulated
    /// atomic counters to the OpenTelemetry metrics system and resets the counters.
    /// </remarks>
    public void PublishMetrics()
    {
        _cache.PublishMetrics();
    }

    /// <summary>
    /// Disposes this test subject and optionally the underlying resources.
    /// </summary>
    /// <remarks>
    /// Disposes the cache instance and conditionally disposes the inner cache
    /// and meter based on the construction parameters.
    /// </remarks>
    public void Dispose()
    {
        _cache.Dispose();
        if (_disposeInner && _innerCache != _cache)
        {
            _innerCache.Dispose();
        }
        if (_disposeMeter)
        {
            Meter.Dispose();
        }
    }
}
