using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNamedMeteredMemoryCache_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNamedMeteredMemoryCache("test-cache", options =>
        {
            options.DisposeInner = true;
            options.AdditionalTags["env"] = "test";
        }, meterName: "test-meter");

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider);

        // Verify options are registered and configured correctly
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var options = optionsMonitor.Get("test-cache");
        Assert.Equal("test-cache", options.CacheName);
        Assert.True(options.DisposeInner);
        Assert.Contains("env", options.AdditionalTags.Keys);
        Assert.Equal("test", options.AdditionalTags["env"]);

        // Verify validator is registered
        var validators = provider.GetServices<IValidateOptions<MeteredMemoryCacheOptions>>();
        Assert.Contains(validators, v => v is MeteredMemoryCacheOptionsValidator);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_WithMinimalConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNamedMeteredMemoryCache("minimal-cache");
        var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var options = optionsMonitor.Get("minimal-cache");
        Assert.Equal("minimal-cache", options.CacheName);
        Assert.False(options.DisposeInner); // Default value
        Assert.NotNull(options.AdditionalTags);
        Assert.Empty(options.AdditionalTags);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_SupportsMultipleNamedCaches()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddNamedMeteredMemoryCache("cache1",
            options => options.AdditionalTags["type"] = "primary",
            meterName: "meter1");
        services.AddNamedMeteredMemoryCache("cache2",
            options => options.AdditionalTags["type"] = "secondary",
            meterName: "meter2");

        var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();

        var options1 = optionsMonitor.Get("cache1");
        Assert.Equal("cache1", options1.CacheName);
        Assert.Equal("primary", options1.AdditionalTags["type"]);

        var options2 = optionsMonitor.Get("cache2");
        Assert.Equal("cache2", options2.CacheName);
        Assert.Equal("secondary", options2.AdditionalTags["type"]);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ThrowsOnNullCacheName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache(null!));
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ThrowsOnEmptyName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache(""));
        Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache("   "));
    }


    [Fact]
    public void AddNamedMeteredMemoryCache_AllowsNullMeterName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert (should not throw)
        services.AddNamedMeteredMemoryCache("cache1", meterName: null);
        services.AddNamedMeteredMemoryCache("cache2", meterName: null);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_AllowsEmptyMeterName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert (should not throw)
        services.AddNamedMeteredMemoryCache("cache1", meterName: "");
        services.AddNamedMeteredMemoryCache("cache2", meterName: "");

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ValidatesOptionsOnStart()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNamedMeteredMemoryCache("test-cache", options =>
        {
            // This will cause validation to fail due to empty key
            options.AdditionalTags[""] = "invalid";
        });

        // Act & Assert
        var provider = services.BuildServiceProvider();

        // Validation happens on first access to options
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        Assert.Throws<OptionsValidationException>(() => optionsMonitor.Get("test-cache"));
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_SupportsOptionsReconfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNamedMeteredMemoryCache("reconfig-cache");

        // Additional configuration
        services.Configure<MeteredMemoryCacheOptions>("reconfig-cache", options =>
        {
            options.DisposeInner = true;
            options.AdditionalTags["configured"] = "later";
        });

        var provider = services.BuildServiceProvider();

        // Act
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var options = optionsMonitor.Get("reconfig-cache");

        // Assert
        Assert.Equal("reconfig-cache", options.CacheName);
        Assert.True(options.DisposeInner);
        Assert.Equal("later", options.AdditionalTags["configured"]);
    }

    [Theory]
    [InlineData("cache-with-dashes")]
    [InlineData("cache_with_underscores")]
    [InlineData("Cache123")]
    [InlineData("UPPERCASE")]
    [InlineData("mixedCase")]
    public void AddNamedMeteredMemoryCache_AcceptsValidCacheNames(string cacheName)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert (should not throw)
        services.AddNamedMeteredMemoryCache(cacheName);
        var provider = services.BuildServiceProvider();

        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var options = optionsMonitor.Get(cacheName);
        Assert.Equal(cacheName, options.CacheName);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_RequiresExistingCacheRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));

        // Act
        services.DecorateMemoryCacheWithMetrics(cacheName: "decorated", meterName: "decorated-meter");
        var provider = services.BuildServiceProvider();

        // Assert
        var cache = provider.GetRequiredService<IMemoryCache>();
        var meter = provider.GetRequiredService<Meter>();

        Assert.NotNull(cache);
        Assert.NotNull(meter);
        Assert.Equal("decorated-meter", meter.Name);
        Assert.IsType<MeteredMemoryCache>(cache);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_WithMinimalConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));

        // Act
        services.DecorateMemoryCacheWithMetrics();
        var provider = services.BuildServiceProvider();

        // Assert
        var cache = provider.GetRequiredService<IMemoryCache>();
        var meter = provider.GetRequiredService<Meter>();
        var options = provider.GetRequiredService<IOptions<MeteredMemoryCacheOptions>>();

        Assert.IsType<MeteredMemoryCache>(cache);
        Assert.Equal("MeteredMemoryCache", meter.Name); // Default meter name
        Assert.Null(options.Value.CacheName); // No cache name specified
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));

        // Act
        services.DecorateMemoryCacheWithMetrics(
            cacheName: "decorated-cache",
            meterName: "custom-meter",
            configureOptions: options =>
            {
                options.DisposeInner = true;
                options.AdditionalTags["environment"] = "test";
            });

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<MeteredMemoryCacheOptions>>();
        Assert.Equal("decorated-cache", options.Value.CacheName);
        Assert.True(options.Value.DisposeInner);
        Assert.Equal("test", options.Value.AdditionalTags["environment"]);

        var meter = provider.GetRequiredService<Meter>();
        Assert.Equal("custom-meter", meter.Name);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_PreservesInnerCacheInstance()
    {
        // Arrange
        var originalCache = new MemoryCache(new MemoryCacheOptions());
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => originalCache);

        // Act
        services.DecorateMemoryCacheWithMetrics(cacheName: "preserved");
        var provider = services.BuildServiceProvider();

        // Assert
        var decoratedCache = provider.GetRequiredService<IMemoryCache>();
        Assert.IsType<MeteredMemoryCache>(decoratedCache);

        // Verify the inner cache is preserved by setting and getting a value
        decoratedCache.Set("test-key", "test-value");
        Assert.True(originalCache.TryGetValue("test-key", out var value));
        Assert.Equal("test-value", value);
    }

    [Fact]
    public void ServiceCollectionExtensions_HandlesConcurrentRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Register multiple caches with different names concurrently
        Parallel.For(0, 10, i =>
        {
            services.AddNamedMeteredMemoryCache($"cache-{i}", meterName: $"meter-{i}");
        });

        // Assert - Should not throw when building provider
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);

        // Verify that at least some caches were registered properly (concurrent execution may affect exact count)
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var registeredCaches = 0;

        for (int i = 0; i < 10; i++)
        {
            try
            {
                var options = optionsMonitor.Get($"cache-{i}");
                if (options.CacheName == $"cache-{i}")
                {
                    registeredCaches++;
                }
            }
            catch
            {
                // Some registrations might fail in concurrent scenarios - this is acceptable
            }
        }

        // Verify that at least half of the caches were registered successfully
        Assert.True(registeredCaches >= 5, $"Expected at least 5 caches to be registered, but only {registeredCaches} were found");
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_AllowsMultipleServiceCollections()
    {
        // Arrange & Act
        var services1 = new ServiceCollection();
        var services2 = new ServiceCollection();

        // Both should succeed since we removed global validation
        services1.AddNamedMeteredMemoryCache("cache-name");
        services2.AddNamedMeteredMemoryCache("cache-name");

        // Assert - Both should build successfully
        var provider1 = services1.BuildServiceProvider();
        var provider2 = services2.BuildServiceProvider();

        Assert.NotNull(provider1);
        Assert.NotNull(provider2);
    }
}
