using System.Diagnostics.Metrics;

using CacheImplementations;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Unit;

/// <summary>
/// Tests for negative scenarios and invalid configurations in MeteredMemoryCache
/// </summary>
public class NegativeConfigurationTests
{
    // Constructor Parameter Validation

    [Fact]
    public void Constructor_NullInnerCache_ThrowsArgumentNullException()
    {
        // Arrange
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MeteredMemoryCache(null!, meter));
        Assert.Equal("innerCache", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullMeter_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MeteredMemoryCache(cache, null!));
        Assert.Equal("meter", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MeteredMemoryCache(cache, meter, (MeteredMemoryCacheOptions)null!));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullInnerCacheWithOptions_ThrowsArgumentNullException()
    {
        // Arrange
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var options = new MeteredMemoryCacheOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MeteredMemoryCache(null!, meter, options));
        Assert.Equal("innerCache", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullMeterWithOptions_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new MeteredMemoryCacheOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MeteredMemoryCache(cache, null!, options));
        Assert.Equal("meter", exception.ParamName);
    }

    // MeteredMemoryCacheOptions Basic Tests

    [Fact]
    public void MeteredMemoryCacheOptions_NullAdditionalTagsKey_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act & Assert - Dictionary Add() throws ArgumentNullException for null keys
        Assert.Throws<ArgumentNullException>(() =>
            options.AdditionalTags.Add(null!, "value"));
    }

    [Fact]
    public void MeteredMemoryCacheOptions_DuplicateAdditionalTagsKey_ThrowsArgumentException()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();
        options.AdditionalTags.Add("key1", "value1");

        // Act & Assert - Dictionary Add() throws ArgumentException for duplicate keys
        var exception = Assert.Throws<ArgumentException>(() =>
            options.AdditionalTags.Add("key1", "value2"));
        Assert.Contains("key", exception.Message); // Message contains "key" somewhere
    }

    // Service Collection Extension Validation

    [Fact]
    public void AddNamedMeteredMemoryCache_NullServiceCollection_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CacheImplementations.ServiceCollectionExtensions.AddNamedMeteredMemoryCache(null!, "test"));
        Assert.Equal("services", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddNamedMeteredMemoryCache_InvalidCacheName_ThrowsArgumentException(string cacheName)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache(cacheName));
        Assert.Equal("cacheName", exception.ParamName);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_NullCacheName_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache(null!));
        Assert.Equal("cacheName", exception.ParamName);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_NullServiceCollection_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CacheImplementations.ServiceCollectionExtensions.DecorateMemoryCacheWithMetrics(null!, "test"));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_NoMemoryCacheRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - This throws InvalidOperationException from our manual decoration implementation
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.DecorateMemoryCacheWithMetrics("test"));
        Assert.Contains("No IMemoryCache registration found", exception.Message);
    }

    // Disposal Scenarios

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert - should not throw
        meteredCache.Dispose();
        meteredCache.Dispose();
        meteredCache.Dispose();
    }

    [Fact]
    public void TryGetValue_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);
        meteredCache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            meteredCache.TryGetValue("key", out _));
    }

    [Fact]
    public void CreateEntry_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);
        meteredCache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            meteredCache.CreateEntry("key"));
    }

    [Fact]
    public void Remove_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);
        meteredCache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            meteredCache.Remove("key"));
    }

    [Fact]
    public void Set_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);
        meteredCache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            meteredCache.Set("key", "value"));
    }

    [Fact]
    public void GetOrCreate_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);
        meteredCache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            meteredCache.GetOrCreate("key", _ => "value"));
    }

    [Fact]
    public void TryGet_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var meteredCache = new MeteredMemoryCache(cache, meter);
        meteredCache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            meteredCache.TryGet<string>("key", out _));
    }

    [Fact]
    public void Dispose_WithDisposeInnerTrue_DisposesInnerCache()
    {
        // Arrange
        var innerCache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var options = new MeteredMemoryCacheOptions { DisposeInner = true };
        var meteredCache = new MeteredMemoryCache(innerCache, meter, options);

        // Act
        meteredCache.Dispose();

        // Assert - inner cache should be disposed
        Assert.Throws<ObjectDisposedException>(() => innerCache.CreateEntry("test"));
    }

    [Fact]
    public void Dispose_WithDisposeInnerFalse_DoesNotDisposeInnerCache()
    {
        // Arrange
        using var innerCache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        var options = new MeteredMemoryCacheOptions { DisposeInner = false };
        var meteredCache = new MeteredMemoryCache(innerCache, meter, options);

        // Act
        meteredCache.Dispose();

        // Assert - inner cache should still be usable
        using var entry = innerCache.CreateEntry("test");
        Assert.NotNull(entry);
    }

    // Null Key Validation

    [Fact]
    public void TryGetValue_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.TryGetValue(null!, out _));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void CreateEntry_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.CreateEntry(null!));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Remove_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.Remove(null!));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Set_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.Set(null!, "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void GetOrCreate_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.GetOrCreate<string>(null!, _ => "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void GetOrCreate_NullFactory_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.GetOrCreate<string>("key", null!));
        Assert.Equal("factory", exception.ParamName);
    }

    [Fact]
    public void TryGet_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            meteredCache.TryGet<string>(null!, out _));
        Assert.Equal("key", exception.ParamName);
    }

    // Edge Case Scenarios

    [Fact]
    public void VeryLongCacheName_DoesNotCauseIssues()
    {
        // Arrange
        var longName = new string('a', 1000);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert - should not throw
        using var meteredCache = new MeteredMemoryCache(cache, meter, longName);
        Assert.NotNull(meteredCache);
    }

    [Fact]
    public void SpecialCharactersInCacheName_DoesNotCauseIssues()
    {
        // Arrange
        var specialName = "cache-with/special\\chars:and\"quotes'and<brackets>";
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert - should not throw
        using var meteredCache = new MeteredMemoryCache(cache, meter, specialName);
        Assert.NotNull(meteredCache);
    }

    [Fact]
    public void MaximumAdditionalTags_DoesNotCauseIssues()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();
        for (int i = 0; i < 100; i++)
        {
            options.AdditionalTags.Add($"tag{i}", $"value{i}");
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert - should not throw
        using var meteredCache = new MeteredMemoryCache(cache, meter, options);
        Assert.NotNull(meteredCache);
    }

    [Fact]
    public void EmptyStringCacheName_IsAccepted()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert - should not throw, empty string is valid
        using var meteredCache = new MeteredMemoryCache(cache, meter, "");
        Assert.NotNull(meteredCache);
    }

    [Fact]
    public void NullCacheName_IsAccepted()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));

        // Act & Assert - should not throw, null is valid (no cache.name tag)
        using var meteredCache = new MeteredMemoryCache(cache, meter, (string?)null);
        Assert.NotNull(meteredCache);
    }

    [Fact]
    public void ServiceProvider_CorrectConfiguration_CreatesCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Meter>(sp => new Meter(SharedUtilities.GetUniqueMeterName("test")));
        services.AddNamedMeteredMemoryCache("test");
        var provider = services.BuildServiceProvider();

        // Act & Assert - should not throw
        var cache = provider.GetRequiredService<IMemoryCache>();
        Assert.NotNull(cache);
        Assert.IsType<MeteredMemoryCache>(cache);
    }

    [Fact]
    public void GetOrCreate_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
        using var meteredCache = new MeteredMemoryCache(cache, meter);

        // Act & Assert - Factory returning null for reference type should throw
        var exception = Assert.Throws<InvalidOperationException>(() =>
            meteredCache.GetOrCreate<string>("key", _ => null!));
        Assert.Contains("Factory returned null", exception.Message);
    }

    // Options Validator Direct Testing

    [Fact]
    public void MeteredMemoryCacheOptionsValidator_ValidOptions_ReturnsSuccess()
    {
        // Arrange
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = { ["environment"] = "test", ["region"] = "us-west" }
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.Failures);
    }

    [Fact]
    public void MeteredMemoryCacheOptionsValidator_EmptyCacheName_ReturnsFailure()
    {
        // Arrange
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "   ", // Whitespace only
            AdditionalTags = { }
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains("CacheName, if specified, must be non-empty.", result.Failures);
    }

    [Fact]
    public void MeteredMemoryCacheOptionsValidator_EmptyTagKey_ReturnsFailure()
    {
        // Arrange
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = { [""] = "value", ["valid-key"] = "valid-value" }
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains("AdditionalTags keys must be non-empty.", result.Failures);
    }

    [Fact]
    public void MeteredMemoryCacheOptionsValidator_MultipleValidationFailures_ReturnsAllFailures()
    {
        // Arrange
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "   ", // Invalid: whitespace only
            AdditionalTags = { [""] = "value", ["  "] = "another-value" } // Invalid: empty and whitespace keys
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Equal(2, result.Failures.Count());
        Assert.Contains("CacheName, if specified, must be non-empty.", result.Failures);
        Assert.Contains("AdditionalTags keys must be non-empty.", result.Failures);
    }

    // Cache Name Normalization Edge Cases

    [Fact]
    public void CacheName_WithLeadingTrailingWhitespace_IsNormalizedCorrectly()
    {
        // Arrange
        using var inner = new MemoryCache(new MemoryCacheOptions());
        using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.normalization"));

        // Act - cache name with leading/trailing whitespace
        var cache = new MeteredMemoryCache(inner, meter, cacheName: "  cache-name  ");

        // Assert - normalized name should be trimmed
        Assert.Equal("cache-name", cache.Name);
    }

    [Fact]
    public void AdditionalTags_InvalidOperations_ThrowsExpectedExceptions()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "test-cache"
        };

        // Act & Assert - Adding null key should throw
        Assert.Throws<ArgumentNullException>(() => options.AdditionalTags.Add(null!, "value"));

        // Adding duplicate keys should throw
        options.AdditionalTags.Add("duplicate", "value1");
        Assert.Throws<ArgumentException>(() => options.AdditionalTags.Add("duplicate", "value2"));
    }
}
