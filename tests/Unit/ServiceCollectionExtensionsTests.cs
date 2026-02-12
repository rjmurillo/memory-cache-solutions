using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Unit;

public class ServiceCollectionExtensionsTests
{

    [Fact]
    public void AddNamedMeteredMemoryCache_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var cacheName = SharedUtilities.GetUniqueCacheName("test-cache");
        var meterName = SharedUtilities.GetUniqueMeterName("test-meter");

        // Act
        services.AddNamedMeteredMemoryCache(cacheName, options =>
        {
            options.DisposeInner = true;
            options.AdditionalTags["env"] = "test";
        }, meterName: meterName);

        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider);

        // Strengthen assertions: Resolve and assert registry availability
        var keyedCache = provider.GetRequiredKeyedService<IMemoryCache>(cacheName);
        Assert.NotNull(keyedCache);
        Assert.IsType<MeteredMemoryCache>(keyedCache);

        // Verify options are registered and configured correctly
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var options = optionsMonitor.Get(cacheName);
        Assert.Equal(cacheName, options.CacheName);
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
        using var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var options = optionsMonitor.Get("minimal-cache");
        Assert.Equal("minimal-cache", options.CacheName);
        Assert.True(options.DisposeInner); // Set to true for owned caches to prevent memory leaks
        Assert.NotNull(options.AdditionalTags);
        Assert.Empty(options.AdditionalTags);

        // Strengthen assertions: Resolve and assert registry availability
        // This addresses PR comment #2331684874 about weak assertions
        var keyedCache = provider.GetRequiredKeyedService<IMemoryCache>("minimal-cache");
        Assert.NotNull(keyedCache);
        Assert.IsType<MeteredMemoryCache>(keyedCache);

        // Assert that the cache is functional
        keyedCache.Set("test-key", "test-value");
        Assert.True(keyedCache.TryGetValue("test-key", out var retrievedValue));
        Assert.Equal("test-value", retrievedValue);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_SupportsMultipleNamedCaches()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var meterName1 = SharedUtilities.GetUniqueMeterName("meter1");
        var meterName2 = SharedUtilities.GetUniqueMeterName("meter2");

        services.AddNamedMeteredMemoryCache("cache1",
            options => options.AdditionalTags["type"] = "primary",
            meterName: meterName1);
        services.AddNamedMeteredMemoryCache("cache2",
            options => options.AdditionalTags["type"] = "secondary",
            meterName: meterName2);

        using var provider = services.BuildServiceProvider();

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

        // Act & Assert - Strengthen assertions with ParamName validation
        var nullEx = Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache(null!));
        Assert.Equal("cacheName", nullEx.ParamName);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ThrowsOnEmptyName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - Strengthen assertions with ParamName validation
        var emptyEx = Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache(""));
        Assert.Equal("cacheName", emptyEx.ParamName);

        var whitespaceEx = Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache("   "));
        Assert.Equal("cacheName", whitespaceEx.ParamName);
    }


    [Fact]
    public void AddNamedMeteredMemoryCache_AllowsNullMeterName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert (should not throw)
        services.AddNamedMeteredMemoryCache("cache1", meterName: null);
        services.AddNamedMeteredMemoryCache("cache2", meterName: null);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);

        // Strengthen assertions: Verify actual cache registration works
        var cache1 = provider.GetRequiredKeyedService<IMemoryCache>("cache1");
        var cache2 = provider.GetRequiredKeyedService<IMemoryCache>("cache2");
        Assert.NotNull(cache1);
        Assert.NotNull(cache2);
        Assert.IsType<MeteredMemoryCache>(cache1);
        Assert.IsType<MeteredMemoryCache>(cache2);
        Assert.NotSame(cache1, cache2);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_AllowsEmptyMeterName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert (should not throw)
        services.AddNamedMeteredMemoryCache("cache1", meterName: "");
        services.AddNamedMeteredMemoryCache("cache2", meterName: "");

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);

        // Strengthen assertions: Verify registry availability and functionality
        var cache1 = provider.GetRequiredKeyedService<IMemoryCache>("cache1");
        var cache2 = provider.GetRequiredKeyedService<IMemoryCache>("cache2");
        Assert.NotNull(cache1);
        Assert.NotNull(cache2);
        Assert.IsType<MeteredMemoryCache>(cache1);
        Assert.IsType<MeteredMemoryCache>(cache2);

        // Verify caches are functional with IMeterFactory fallback
        cache1.Set("test-key", "value1");
        Assert.True(cache1.TryGetValue("test-key", out _));
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
        using var provider = services.BuildServiceProvider();

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

        using var provider = services.BuildServiceProvider();

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
        using var provider = services.BuildServiceProvider();

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
        var decoratedMeterName = SharedUtilities.GetUniqueMeterName("decorated-meter");
        services.DecorateMemoryCacheWithMetrics(cacheName: "decorated", meterName: decoratedMeterName);
        using var provider = services.BuildServiceProvider();

        // Assert
        var cache = provider.GetRequiredService<IMemoryCache>();

        Assert.NotNull(cache);
        Assert.IsType<MeteredMemoryCache>(cache);

        // Assert cache name preservation in decorator
        var meteredCache = (MeteredMemoryCache)cache;
        Assert.Equal("decorated", meteredCache.Name);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_WithMinimalConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));

        // Act
        services.DecorateMemoryCacheWithMetrics();
        using var provider = services.BuildServiceProvider();

        // Assert
        var cache = provider.GetRequiredService<IMemoryCache>();

        Assert.IsType<MeteredMemoryCache>(cache);
        Assert.Null(((MeteredMemoryCache)cache).Name); // No cache name specified
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));

        // Act
        var customMeterName = SharedUtilities.GetUniqueMeterName("custom-meter");
        services.DecorateMemoryCacheWithMetrics(
            cacheName: "decorated-cache",
            meterName: customMeterName,
            configureOptions: options =>
            {
                options.DisposeInner = true;
                options.AdditionalTags["environment"] = "test";
            });

        using var provider = services.BuildServiceProvider();

        // Assert - Test the actual decorated cache instead of trying to access internal options
        var decoratedCache = provider.GetRequiredService<IMemoryCache>();
        Assert.IsType<MeteredMemoryCache>(decoratedCache);

        // Verify cache name is preserved in the decorated cache
        var meteredCache = (MeteredMemoryCache)decoratedCache;
        Assert.Equal("decorated-cache", meteredCache.Name);
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
        using var provider = services.BuildServiceProvider();

        // Assert
        var decoratedCache = provider.GetRequiredService<IMemoryCache>();
        Assert.IsType<MeteredMemoryCache>(decoratedCache);

        // Assert cache name preservation in decorator
        var meteredCache = (MeteredMemoryCache)decoratedCache;
        Assert.Equal("preserved", meteredCache.Name);

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
        var lockObj = new object();

        // Act - Register multiple caches with same meter name to avoid keyed service conflicts
        // Use locking to synchronize concurrent registrations since ServiceCollection is not thread-safe
        var sharedMeterName = SharedUtilities.GetUniqueMeterName("shared-meter");
        Parallel.For(0, 10, i =>
        {
            lock (lockObj)
            {
                services.AddNamedMeteredMemoryCache($"cache-{i}", meterName: sharedMeterName);
            }
        });

        // Assert - Should not throw when building provider
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);

        // Strengthen assertions: Verify registry availability for concurrent registrations
        // Test a few random cache registrations to ensure they work
        var cache0 = provider.GetRequiredKeyedService<IMemoryCache>("cache-0");
        var cache5 = provider.GetRequiredKeyedService<IMemoryCache>("cache-5");
        var cache9 = provider.GetRequiredKeyedService<IMemoryCache>("cache-9");

        Assert.NotNull(cache0);
        Assert.NotNull(cache5);
        Assert.NotNull(cache9);
        Assert.IsType<MeteredMemoryCache>(cache0);
        Assert.IsType<MeteredMemoryCache>(cache5);
        Assert.IsType<MeteredMemoryCache>(cache9);

        // Verify that all caches were registered properly (with synchronization, all should succeed)
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<MeteredMemoryCacheOptions>>();
        var registeredCaches = 0;

        for (int i = 0; i < 10; i++)
        {
            var options = optionsMonitor.Get($"cache-{i}");
            if (options.CacheName == $"cache-{i}")
            {
                registeredCaches++;
            }
        }

        // With proper synchronization, all caches should be registered successfully
        Assert.Equal(10, registeredCaches);
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
        using var provider1 = services1.BuildServiceProvider();
        using var provider2 = services2.BuildServiceProvider();

        Assert.NotNull(provider1);
        Assert.NotNull(provider2);
    }
}
