using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

namespace Unit.Shared;

/// <summary>
/// Test subject wrapper for the original <see cref="MeteredMemoryCache"/> implementation.
/// Provides a standardized interface for testing the traditional cache implementation
/// with immediate metric publishing using <see cref="Counter{T}"/> instances.
/// </summary>
/// <remarks>
/// This test subject enables shared test scenarios to be run against the original
/// metered cache implementation, which uses OpenTelemetry counters for immediate
/// metric emission on each cache operation.
/// </remarks>
public sealed class MeteredCacheTestSubject : IMeteredCacheTestSubject
{
    private readonly MeteredMemoryCache _cache;
    private readonly IMemoryCache _innerCache;
    private readonly bool _disposeInner;
    private readonly bool _disposeMeter;

    /// <summary>
    /// Gets the underlying <see cref="IMemoryCache"/> instance being tested.
    /// </summary>
    /// <value>The <see cref="MeteredMemoryCache"/> instance.</value>
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
    /// <value>Always returns <see langword="true"/> since <see cref="MeteredMemoryCache"/> always has metrics enabled.</value>
    public bool MetricsEnabled => true; // MeteredMemoryCache always has metrics enabled

    /// <summary>
    /// Gets the implementation type name for test identification.
    /// </summary>
    /// <value>Always returns "MeteredMemoryCache".</value>
    public string ImplementationType => "MeteredMemoryCache";

    /// <summary>
    /// Initializes a new instance of the <see cref="MeteredCacheTestSubject"/> class with basic parameters.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="cacheName">Optional logical name for the cache instance.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed.</param>
    public MeteredCacheTestSubject(
        IMemoryCache? innerCache = null,
        Meter? meter = null,
        string? cacheName = null,
        bool disposeInner = true)
    {
        _innerCache = innerCache ?? new MemoryCache(new MemoryCacheOptions());
        _disposeInner = disposeInner;
        _disposeMeter = meter == null; // Only dispose meter if we created it
        Meter = meter ?? new Meter($"test.metered.{Guid.NewGuid()}");
        _cache = new MeteredMemoryCache(_innerCache, Meter, cacheName, disposeInner);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MeteredCacheTestSubject"/> class with options.
    /// </summary>
    /// <param name="innerCache">The underlying <see cref="IMemoryCache"/> to wrap. If <see langword="null"/>, a new <see cref="MemoryCache"/> is created.</param>
    /// <param name="meter">The <see cref="Meter"/> instance to use. If <see langword="null"/>, a new meter is created with a unique name.</param>
    /// <param name="options">The <see cref="MeteredMemoryCacheOptions"/> to configure the cache instance.</param>
    /// <param name="disposeInner">Whether to dispose the <paramref name="innerCache"/> when this instance is disposed.</param>
    public MeteredCacheTestSubject(
        IMemoryCache? innerCache,
        Meter? meter,
        MeteredMemoryCacheOptions options,
        bool disposeInner = true)
    {
        _innerCache = innerCache ?? new MemoryCache(new MemoryCacheOptions());
        _disposeInner = disposeInner;
        _disposeMeter = meter == null; // Only dispose meter if we created it
        Meter = meter ?? new Meter($"test.metered.{Guid.NewGuid()}");
        _cache = new MeteredMemoryCache(_innerCache, Meter, options);
    }

    /// <summary>
    /// Gets the current cache statistics if supported by the implementation.
    /// </summary>
    /// <returns>Always returns <see langword="null"/> since <see cref="MeteredMemoryCache"/> doesn't support statistics retrieval.</returns>
    /// <remarks>
    /// The original <see cref="MeteredMemoryCache"/> implementation doesn't provide
    /// a way to retrieve current statistics. Metrics are published immediately
    /// to OpenTelemetry counters on each operation.
    /// </remarks>
    public object? GetCurrentStatistics()
    {
        // MeteredMemoryCache doesn't support GetCurrentStatistics
        return null;
    }

    /// <summary>
    /// Publishes accumulated metrics if supported by the implementation.
    /// </summary>
    /// <remarks>
    /// For <see cref="MeteredMemoryCache"/>, this method does nothing since
    /// metrics are published immediately to OpenTelemetry counters on each
    /// cache operation rather than being accumulated.
    /// </remarks>
    public void PublishMetrics()
    {
        // MeteredMemoryCache doesn't support periodic publishing
        // Metrics are published immediately
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
        if (!_disposeInner && _innerCache != _cache)
        {
            _innerCache.Dispose();
        }
        if (_disposeMeter)
        {
            Meter.Dispose();
        }
    }
}
