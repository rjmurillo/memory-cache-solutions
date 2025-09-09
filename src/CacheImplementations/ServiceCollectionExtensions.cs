// Copyright (c) 2025, MeteredMemoryCache contributors
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheImplementations;

/// <summary>
/// Extension methods for registering MeteredMemoryCache in DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named MeteredMemoryCache with metrics in the service collection using options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheName">The logical cache name (for metrics tag).</param>
    /// <param name="configureOptions">Optional MeteredMemoryCacheOptions configuration.</param>
    /// <param name="meterName">Optional meter name (defaults to "MeteredMemoryCache").</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddNamedMeteredMemoryCache(
        this IServiceCollection services,
        string cacheName,
        Action<MeteredMemoryCacheOptions>? configureOptions = null,
        string? meterName = null)
    {
        if (string.IsNullOrWhiteSpace(cacheName))
            throw new ArgumentException("Cache name must be non-empty", nameof(cacheName));

        // Static sets for duplicate validation
        if (!s_cacheNames.Add(cacheName))
            throw new InvalidOperationException($"A cache with the name '{cacheName}' is already registered.");
        if (!string.IsNullOrEmpty(meterName) && !s_meterNames.Add(meterName))
            throw new InvalidOperationException($"A meter with the name '{meterName}' is already registered.");

        // Register options with validation
        services.AddOptions<MeteredMemoryCacheOptions>(cacheName)
            .Configure(opts =>
            {
                opts.CacheName = cacheName;
                configureOptions?.Invoke(opts);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register the options validator
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MeteredMemoryCacheOptions>, MeteredMemoryCacheOptionsValidator>());

        services.TryAddSingleton<NamedMemoryCacheRegistry>();

        services.PostConfigure<NamedMemoryCacheRegistry>(reg =>
        {
            reg.TryAdd(cacheName, () =>
            {
                var meter = new Meter(meterName ?? "MeteredMemoryCache");
                var options = reg.ServiceProvider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>().Get(cacheName);
                return new MeteredMemoryCache(new MemoryCache(new MemoryCacheOptions()), meter, options);
            }, meterName);
        });

        return services;
    }

    /// <summary>
    /// Decorates an existing IMemoryCache registration with MeteredMemoryCache for metrics using options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheName">Optional cache name for metrics tag.</param>
    /// <param name="meterName">Optional meter name.</param>
    /// <param name="configureOptions">Optional MeteredMemoryCacheOptions configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection DecorateMemoryCacheWithMetrics(
        this IServiceCollection services,
        string? cacheName = null,
        string? meterName = null,
        Action<MeteredMemoryCacheOptions>? configureOptions = null)
    {
        services.TryAddSingleton<Meter>(sp =>
            new Meter(meterName ?? "MeteredMemoryCache"));

        services.Configure<MeteredMemoryCacheOptions>(opts =>
        {
            opts.CacheName = cacheName;
            configureOptions?.Invoke(opts);
        });

        services.Decorate<IMemoryCache>((inner, sp) =>
        {
            var meter = sp.GetRequiredService<Meter>();
            var options = sp.GetRequiredService<IOptions<MeteredMemoryCacheOptions>>().Value;
            return new MeteredMemoryCache(inner, meter, options);
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

        // For options-based resolution
        public IServiceProvider ServiceProvider { get; set; } = default!;
    }
}
