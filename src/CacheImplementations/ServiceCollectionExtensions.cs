using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CacheImplementations;

/// <summary>
/// Extension methods for registering MeteredMemoryCache in DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named MeteredMemoryCache with metrics in the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheName">The logical cache name (for metrics tag).</param>
    /// <param name="configureOptions">Optional MemoryCacheOptions configuration.</param>
    /// <param name="meterName">Optional meter name (defaults to "MeteredMemoryCache").</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddNamedMeteredMemoryCache(
        this IServiceCollection services,
        string cacheName,
        Action<MemoryCacheOptions>? configureOptions = null,
        string? meterName = null)
    {
        if (string.IsNullOrWhiteSpace(cacheName))
            throw new ArgumentException("Cache name must be non-empty", nameof(cacheName));

        // Register the Meter as a singleton if not already present
        services.TryAddSingleton<Meter>(sp =>
            new Meter(meterName ?? "MeteredMemoryCache"));

        // Register the IMemoryCache for this name
        services.AddSingleton<IMemoryCache>(sp =>
        {
            var options = new MemoryCacheOptions();
            configureOptions?.Invoke(options);
            return new MemoryCache(options);
        });

        // Decorate with MeteredMemoryCache
        services.Decorate<IMemoryCache>((inner, sp) =>
        {
            var meter = sp.GetRequiredService<Meter>();
            return new MeteredMemoryCache(inner, meter, cacheName, disposeInner: true);
        });

        return services;
    }

    /// <summary>
    /// Decorates an existing IMemoryCache registration with MeteredMemoryCache for metrics.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheName">Optional cache name for metrics tag.</param>
    /// <param name="meterName">Optional meter name.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection DecorateMemoryCacheWithMetrics(
        this IServiceCollection services,
        string? cacheName = null,
        string? meterName = null)
    {
        services.TryAddSingleton<Meter>(sp =>
            new Meter(meterName ?? "MeteredMemoryCache"));

        services.Decorate<IMemoryCache>((inner, sp) =>
        {
            var meter = sp.GetRequiredService<Meter>();
            return new MeteredMemoryCache(inner, meter, cacheName, disposeInner: false);
        });

        return services;
    }
}
