using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Unit;

/// <summary>
/// Regression tests for SingleFlightLazyCache null safety fixes.
/// </summary>
public class SingleFlightLazyCacheNullSafetyTests
{
    /// <summary>
    /// Tests that null factory results for reference types throw appropriate exceptions.
    /// This test verifies the fix for the null safety issue where factory returning null
    /// for reference types could cause unexpected behavior.
    /// </summary>
    [Fact]
    public void FactoryReturningNullForReferenceType_ShouldThrowInvalidOperationException()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        string? NullFactory() => null;

        // Should throw when factory returns null for reference type
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            sfl.GetOrCreate("null-key", TimeSpan.FromMinutes(1), NullFactory);
        });

        Assert.Contains("Factory returned null for a reference type", exception.Message);
    }

    /// <summary>
    /// Tests that null factory results for nullable reference types are handled correctly.
    /// </summary>
    [Fact]
    public void FactoryReturningNullForNullableReferenceType_ShouldThrowInvalidOperationException()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        string? NullFactory() => null;

        // Should throw when factory returns null for nullable reference type
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            sfl.GetOrCreate("null-nullable-key", TimeSpan.FromMinutes(1), NullFactory);
        });

        Assert.Contains("Factory returned null for a reference type", exception.Message);
    }

    /// <summary>
    /// Tests that null factory results for value types are handled correctly (should not throw).
    /// </summary>
    [Fact]
    public void FactoryReturningNullForValueType_ShouldNotThrow()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        int? NullableValueFactory() => null;

        // Should not throw for nullable value types
        var result = sfl.GetOrCreate("null-value-key", TimeSpan.FromMinutes(1), NullableValueFactory);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that valid factory results work correctly for reference types.
    /// </summary>
    [Fact]
    public void FactoryReturningValidReferenceType_ShouldWorkCorrectly()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        string ValidFactory() => "valid-result";

        var result = sfl.GetOrCreate("valid-key", TimeSpan.FromMinutes(1), ValidFactory);
        Assert.Equal("valid-result", result);
    }

    /// <summary>
    /// Tests that valid factory results work correctly for value types.
    /// </summary>
    [Fact]
    public void FactoryReturningValidValueType_ShouldWorkCorrectly()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        int ValidFactory() => 42;

        var result = sfl.GetOrCreate("valid-value-key", TimeSpan.FromMinutes(1), ValidFactory);
        Assert.Equal(42, result);
    }

    /// <summary>
    /// Tests that the async version also handles null safety correctly.
    /// </summary>
    [Fact]
    public async Task AsyncFactoryReturningNullForReferenceType_ShouldThrowInvalidOperationException()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        async Task<string?> NullAsyncFactory()
        {
            await Task.Yield();
            return null;
        }

        // Should throw when async factory returns null for reference type
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await sfl.GetOrCreateAsync("null-async-key", TimeSpan.FromMinutes(1), NullAsyncFactory);
        });

        Assert.Contains("Factory returned null for a reference type", exception.Message);
    }

    /// <summary>
    /// Tests that the async version works correctly with valid results.
    /// </summary>
    [Fact]
    public async Task AsyncFactoryReturningValidReferenceType_ShouldWorkCorrectly()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        async Task<string> ValidAsyncFactory()
        {
            await Task.Yield();
            return "valid-async-result";
        }

        var result = await sfl.GetOrCreateAsync("valid-async-key", TimeSpan.FromMinutes(1), ValidAsyncFactory);
        Assert.Equal("valid-async-result", result);
    }

    /// <summary>
    /// Tests that caching works correctly with null safety checks.
    /// </summary>
    [Fact]
    public void CachingWithNullSafety_ShouldWorkCorrectly()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sfl = new SingleFlightLazyCache(cache);

        var callCount = 0;
        string ValidFactory()
        {
            callCount++;
            return $"result-{callCount}";
        }

        // First call
        var result1 = sfl.GetOrCreate("cache-key", TimeSpan.FromMinutes(1), ValidFactory);
        Assert.Equal("result-1", result1);
        Assert.Equal(1, callCount);

        // Second call should use cached value
        var result2 = sfl.GetOrCreate("cache-key", TimeSpan.FromMinutes(1), ValidFactory);
        Assert.Equal("result-1", result2); // Should be cached
        Assert.Equal(1, callCount); // Factory should not be called again
    }
}
