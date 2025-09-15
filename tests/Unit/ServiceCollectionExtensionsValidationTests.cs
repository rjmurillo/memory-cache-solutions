using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Unit;

/// <summary>
/// Regression tests for ServiceCollectionExtensions validation fixes.
/// </summary>
public class ServiceCollectionExtensionsValidationTests
{
    /// <summary>
    /// Tests that DecorateMemoryCacheWithMetrics validates options properly.
    /// This test verifies the fix for missing validation in the decoration method.
    /// </summary>
    [Fact]
    public void DecorateMemoryCacheWithMetrics_WithInvalidOptions_ShouldThrowValidationException()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();

        // Configure with invalid options (empty cache name)
        services.DecorateMemoryCacheWithMetrics(cacheName: "", meterName: "test-meter", opts =>
        {
            opts.CacheName = ""; // Invalid: empty cache name
        });

        var provider = services.BuildServiceProvider();

        // Should throw validation exception when trying to get the service
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            provider.GetRequiredService<IMemoryCache>();
        });

        Assert.Contains("CacheName", exception.Message);
    }

    /// <summary>
    /// Tests that DecorateMemoryCacheWithMetrics handles missing IMemoryCache registration correctly.
    /// </summary>
    [Fact]
    public void DecorateMemoryCacheWithMetrics_WithNoMemoryCacheRegistration_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();
        // Don't add IMemoryCache

        // Should throw when trying to decorate non-existent IMemoryCache
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            services.DecorateMemoryCacheWithMetrics(cacheName: "test-cache", meterName: "test-meter");
        });

        Assert.Contains("No IMemoryCache registration found", exception.Message);
        Assert.Contains("Register IMemoryCache before calling", exception.Message);
    }

    /// <summary>
    /// Tests that DecorateMemoryCacheWithMetrics works correctly with valid configuration.
    /// </summary>
    [Fact]
    public void DecorateMemoryCacheWithMetrics_WithValidConfiguration_ShouldWorkCorrectly()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();

        services.DecorateMemoryCacheWithMetrics(cacheName: "test-cache", meterName: "test-meter", opts =>
        {
            opts.CacheName = "test-cache";
            opts.AdditionalTags.Add("environment", "test");
        });

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        // Should be able to use the cache
        cache.Set("test-key", "test-value");
        Assert.True(cache.TryGetValue("test-key", out var value));
        Assert.Equal("test-value", value);
    }

    /// <summary>
    /// Tests that options names are unique to prevent collisions.
    /// </summary>
    [Fact]
    public void DecorateMemoryCacheWithMetrics_MultipleCalls_ShouldCreateUniqueOptionsNames()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();

        // Multiple decoration calls should not conflict
        services.DecorateMemoryCacheWithMetrics(cacheName: "cache1", meterName: "meter1");
        services.DecorateMemoryCacheWithMetrics(cacheName: "cache2", meterName: "meter2");

        var provider = services.BuildServiceProvider();

        // Should be able to build without conflicts
        var cache = provider.GetRequiredService<IMemoryCache>();
        Assert.NotNull(cache);
    }

    /// <summary>
    /// Tests that AddNamedMeteredMemoryCache validates options properly.
    /// </summary>
    [Fact]
    public void AddNamedMeteredMemoryCache_WithInvalidOptions_ShouldThrowValidationException()
    {
        var services = new ServiceCollection();

        // Configure with invalid options (empty cache name)
        services.AddNamedMeteredMemoryCache("test-cache", opts =>
        {
            opts.CacheName = ""; // Invalid: empty cache name
        }, "test-meter");

        var provider = services.BuildServiceProvider();

        // Should throw validation exception when trying to get the service
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            provider.GetRequiredService<IMemoryCache>();
        });

        Assert.Contains("CacheName", exception.Message);
    }

    /// <summary>
    /// Tests that AddNamedMeteredMemoryCache works correctly with valid configuration.
    /// </summary>
    [Fact]
    public void AddNamedMeteredMemoryCache_WithValidConfiguration_ShouldWorkCorrectly()
    {
        var services = new ServiceCollection();

        services.AddNamedMeteredMemoryCache("test-cache", opts =>
        {
            opts.CacheName = "test-cache";
            opts.AdditionalTags.Add("environment", "test");
        }, "test-meter");

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        // Should be able to use the cache
        cache.Set("test-key", "test-value");
        Assert.True(cache.TryGetValue("test-key", out var value));
        Assert.Equal("test-value", value);
    }
}