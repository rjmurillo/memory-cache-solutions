// Copyright (c) 2025, MeteredMemoryCache contributors
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

        var effectiveMeterName = meterName ?? "MeteredMemoryCache";

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

        // Register the meter if not already registered
        services.TryAddSingleton<Meter>(sp => new Meter(effectiveMeterName));

        // Register the keyed cache service
        services.AddKeyedSingleton<IMemoryCache>(cacheName, (sp, key) =>
        {
            var keyString = (string)key!;
            var innerCache = new MemoryCache(new MemoryCacheOptions());
            var meter = sp.GetRequiredService<Meter>();
            var options = sp.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>().Get(keyString);
            return new MeteredMemoryCache(innerCache, meter, options);
        });

        // Try to register as singleton for cases where there's only one named cache
        // Use TryAddSingleton to avoid concurrency issues
        services.TryAddSingleton<IMemoryCache>(sp => sp.GetRequiredKeyedService<IMemoryCache>(cacheName));

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
        var effectiveMeterName = meterName ?? "MeteredMemoryCache";

        // Register the meter if not already registered
        services.TryAddSingleton<Meter>(sp => new Meter(effectiveMeterName));

        // Configure options
        services.Configure<MeteredMemoryCacheOptions>(opts =>
        {
            opts.CacheName = cacheName;
            configureOptions?.Invoke(opts);
        });

        // Register the options validator
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MeteredMemoryCacheOptions>, MeteredMemoryCacheOptionsValidator>());

        // Decorate the existing IMemoryCache registration
        services.Decorate<IMemoryCache>((inner, sp) =>
        {
            var meter = sp.GetRequiredService<Meter>();
            var options = sp.GetRequiredService<IOptions<MeteredMemoryCacheOptions>>().Value;
            return new MeteredMemoryCache(inner, meter, options);
        });

        return services;
    }
}
