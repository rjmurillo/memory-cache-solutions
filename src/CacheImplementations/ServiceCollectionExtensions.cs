// Copyright (c) 2025, MeteredMemoryCache contributors
using System;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheImplementations;

/// <summary>
/// Extension methods for registering <see cref="MeteredMemoryCache"/> instances with dependency injection containers.
/// Provides fluent APIs for both named cache registration and decoration of existing <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> services.
/// </summary>
/// <remarks>
/// These extensions integrate with the .NET options pattern and validation framework to ensure correct configuration
/// at application startup. All registrations include automatic <see cref="System.Diagnostics.Metrics.Meter"/> setup
/// and <see cref="MeteredMemoryCacheOptions"/> validation.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named <see cref="MeteredMemoryCache"/> instance with comprehensive dependency injection setup.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to. Cannot be <see langword="null"/>.</param>
    /// <param name="cacheName">The logical name for the cache instance, used for keyed service resolution and as the "cache.name" metric tag. Cannot be <see langword="null"/> or whitespace.</param>
    /// <param name="configureOptions">Optional delegate to configure <see cref="MeteredMemoryCacheOptions"/>. If <see langword="null"/>, default options will be used.</param>
    /// <param name="meterName">Optional name for the <see cref="System.Diagnostics.Metrics.Meter"/> instance. If <see langword="null"/>, defaults to "MeteredMemoryCache".</param>
    /// <returns>The <paramref name="services"/> collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cacheName"/> is <see langword="null"/>, empty, or contains only whitespace.</exception>
    /// <remarks>
    /// <para>
    /// This method performs comprehensive service registration including:
    /// <list type="bullet">
    /// <item><description>Named options configuration with validation using <see cref="Microsoft.Extensions.Options.OptionsBuilder{TOptions}.ValidateDataAnnotations"/> and <see cref="Microsoft.Extensions.Options.OptionsBuilder{TOptions}.ValidateOnStart"/></description></item>
    /// <item><description>Keyed service registration for multi-cache scenarios using <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionKeyedServiceExtensions.AddKeyedSingleton{TService}(IServiceCollection, object, System.Func{IServiceProvider, object, TService})"/></description></item>
    /// <item><description>Fallback singleton registration for single-cache applications</description></item>
    /// <item><description>Automatic <see cref="System.Diagnostics.Metrics.Meter"/> registration if not already present</description></item>
    /// <item><description>Options validator registration using <see cref="MeteredMemoryCacheOptionsValidator"/></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Use <see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService{T}(IServiceProvider, object)"/> 
    /// to resolve specific named caches, or standard <see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService{T}(IServiceProvider)"/> 
    /// if only one cache is registered.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic registration
    /// services.AddNamedMeteredMemoryCache("user-cache");
    /// 
    /// // With custom configuration
    /// services.AddNamedMeteredMemoryCache("product-cache", opts =>
    /// {
    ///     opts.AdditionalTags["service"] = "catalog-api";
    ///     opts.AdditionalTags["environment"] = "production";
    /// });
    /// 
    /// // Usage in controllers or services
    /// public class UserService
    /// {
    ///     public UserService([FromKeyedServices("user-cache")] IMemoryCache cache) { }
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddNamedMeteredMemoryCache(
        this IServiceCollection services,
        string cacheName,
        Action<MeteredMemoryCacheOptions>? configureOptions = null,
        string? meterName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(cacheName))
            throw new ArgumentException("Cache name must be non-empty", nameof(cacheName));

        var effectiveMeterName = meterName ?? nameof(MeteredMemoryCache);

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
    /// Decorates an existing <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> registration with <see cref="MeteredMemoryCache"/> to add comprehensive metrics.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> containing the existing cache registration. Cannot be <see langword="null"/>.</param>
    /// <param name="cacheName">Optional logical name for the cache instance, used as the "cache.name" metric tag. If <see langword="null"/>, no cache name tag will be added.</param>
    /// <param name="meterName">Optional name for the <see cref="System.Diagnostics.Metrics.Meter"/> instance. If <see langword="null"/>, defaults to "MeteredMemoryCache".</param>
    /// <param name="configureOptions">Optional delegate to configure <see cref="MeteredMemoryCacheOptions"/>. If <see langword="null"/>, default options will be used.</param>
    /// <returns>The <paramref name="services"/> collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> registration is found in the service collection.</exception>
    /// <remarks>
    /// <para>
    /// This method replaces an existing <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> registration with a 
    /// <see cref="MeteredMemoryCache"/> decorator that wraps the original cache. The decorator preserves all functionality
    /// of the original cache while adding OpenTelemetry metrics emission.
    /// </para>
    /// <para>
    /// The decoration process:
    /// <list type="number">
    /// <item><description>Locates the existing <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> service descriptor</description></item>
    /// <item><description>Removes the original registration from the service collection</description></item>
    /// <item><description>Creates a new registration that instantiates the original cache and wraps it with <see cref="MeteredMemoryCache"/></description></item>
    /// <item><description>Preserves the original service lifetime (Singleton, Scoped, or Transient)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This approach is ideal for adding metrics to existing applications where cache registration is already established,
    /// such as when using <c>services.AddMemoryCache()</c> or custom cache configurations.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Decorate existing cache registration
    /// services.AddMemoryCache();
    /// services.DecorateMemoryCacheWithMetrics("api-cache");
    /// 
    /// // With custom options
    /// services.AddMemoryCache();
    /// services.DecorateMemoryCacheWithMetrics("user-cache", configureOptions: opts =>
    /// {
    ///     opts.DisposeInner = false; // Don't dispose shared cache
    ///     opts.AdditionalTags["component"] = "user-service";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection DecorateMemoryCacheWithMetrics(
        this IServiceCollection services,
        string? cacheName = null,
        string? meterName = null,
        Action<MeteredMemoryCacheOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

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

        // Manual decoration - find existing IMemoryCache registration and replace it
        var existingDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryCache));
        if (existingDescriptor == null)
        {
            throw new InvalidOperationException("No IMemoryCache registration found. Register IMemoryCache before calling DecorateMemoryCacheWithMetrics.");
        }

        // Remove the existing registration
        services.Remove(existingDescriptor);

        // Create new descriptor that decorates the original
        var decoratedDescriptor = new ServiceDescriptor(
            typeof(IMemoryCache),
            sp =>
            {
                var innerCache = CreateInnerCache(existingDescriptor, sp);
                var meter = sp.GetRequiredService<Meter>();
                var options = sp.GetRequiredService<IOptions<MeteredMemoryCacheOptions>>().Value;
                return new MeteredMemoryCache(innerCache, meter, options);
            },
            existingDescriptor.Lifetime);

        // Add the decorated registration
        services.Add(decoratedDescriptor);

        return services;
    }

    private static IMemoryCache CreateInnerCache(ServiceDescriptor existingDescriptor, IServiceProvider serviceProvider)
    {
        if (existingDescriptor.ImplementationType != null)
        {
            return (IMemoryCache)ActivatorUtilities.CreateInstance(serviceProvider, existingDescriptor.ImplementationType);
        }

        if (existingDescriptor.ImplementationFactory != null)
        {
            return (IMemoryCache)existingDescriptor.ImplementationFactory(serviceProvider);
        }

        if (existingDescriptor.ImplementationInstance is IMemoryCache instance)
        {
            return instance;
        }

        throw new InvalidOperationException("Unable to resolve inner IMemoryCache instance.");
    }
}
