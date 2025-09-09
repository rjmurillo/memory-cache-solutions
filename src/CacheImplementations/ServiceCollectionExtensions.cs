using System.Collections.Concurrent;
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

        // Static sets for duplicate validation
        if (!s_cacheNames.Add(cacheName))
            throw new InvalidOperationException($"A cache with the name '{cacheName}' is already registered.");
        if (!string.IsNullOrEmpty(meterName) && !s_meterNames.Add(meterName))
            throw new InvalidOperationException($"A meter with the name '{meterName}' is already registered.");

        services.TryAddSingleton<NamedMemoryCacheRegistry>();

        services.PostConfigure<NamedMemoryCacheRegistry>(reg =>
        {
            reg.TryAdd(cacheName, () =>
            {
                var meter = new Meter(meterName ?? "MeteredMemoryCache");
                var options = new MemoryCacheOptions();
                configureOptions?.Invoke(options);
                var inner = new MemoryCache(options);
                return new MeteredMemoryCache(inner, meter, cacheName, disposeInner: true);
            }, meterName);
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

    // Static sets for duplicate validation
    private static readonly HashSet<string> s_cacheNames = new(StringComparer.Ordinal);
    private static readonly HashSet<string> s_meterNames = new(StringComparer.Ordinal);

    // Registry for named caches and meters
    private sealed class NamedMemoryCacheRegistry
    {
        private readonly ConcurrentDictionary<string, (Func<IMemoryCache> Factory, string? MeterName)> _caches = new(StringComparer.Ordinal);

        public void TryAdd(string name, Func<IMemoryCache> factory, string? meterName)
        {
            _caches.TryAdd(name, (factory, meterName));
        }

    }

}
