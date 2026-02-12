using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using CacheImplementations;

namespace BasicUsage;

/// <summary>
/// Basic usage example demonstrating MeteredMemoryCache with simple cache operations.
/// This example shows how to:
/// - Configure MeteredMemoryCache with dependency injection
/// - Set up OpenTelemetry metrics collection
/// - Perform basic cache operations (get, set, remove)
/// - Observe metrics being emitted automatically
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("MeteredMemoryCache Basic Usage Example");
        Console.WriteLine("=====================================");

        // Create host builder with services
        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Configure OpenTelemetry metrics
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(MeteredMemoryCache.MeterName) // Use the standard meter name
                .AddConsoleExporter());

        // Register MeteredMemoryCache
        builder.Services.AddNamedMeteredMemoryCache("basic-cache",
            configureOptions: options =>
            {
                // Configure additional tags for metrics
                options.AdditionalTags["environment"] = "demo";
                options.AdditionalTags["component"] = "basic-usage";
            });

        // Register our demo service
        builder.Services.AddTransient<CacheDemo>();

        // Build and run
        using var host = builder.Build();

        var demo = host.Services.GetRequiredService<CacheDemo>();

        await demo.RunDemoAsync();

        Console.WriteLine("\nDemo completed. Press any key to exit...");
        Console.ReadKey();
    }
}

/// <summary>
/// Demonstrates basic cache operations with automatic metrics collection.
/// </summary>
public class CacheDemo
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheDemo> _logger;

    public CacheDemo(IMemoryCache cache, ILogger<CacheDemo> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task RunDemoAsync()
    {
        _logger.LogInformation("Starting cache demonstration...");

        // Demonstrate cache operations
        await DemonstrateCacheMiss();
        await DemonstrateCacheHit();
        await DemonstrateEviction();
        await DemonstrateGetOrCreate();

        _logger.LogInformation("Cache demonstration completed.");

        // Wait for metrics to be exported
        await Task.Delay(3000);
    }

    private async Task DemonstrateCacheMiss()
    {
        _logger.LogInformation("=== Cache Miss Demonstration ===");

        // This will record a cache miss
        var result = _cache.TryGetValue("user:123", out var value);

        _logger.LogInformation("Tried to get 'user:123': Found = {Found}, Value = {Value}",
            result, value);

        await Task.Delay(500);
    }

    private async Task DemonstrateCacheHit()
    {
        _logger.LogInformation("=== Cache Hit Demonstration ===");

        // Set a value
        var userData = new { Id = 123, Name = "John Doe", Email = "john@example.com" };
        _cache.Set("user:123", userData, TimeSpan.FromMinutes(5));

        _logger.LogInformation("Stored user data for 'user:123'");

        // Get it back (this will record a cache hit)
        var found = _cache.TryGetValue("user:123", out var retrievedValue);

        _logger.LogInformation("Retrieved 'user:123': Found = {Found}, Value = {Value}",
            found, retrievedValue);

        await Task.Delay(500);
    }

    private async Task DemonstrateEviction()
    {
        _logger.LogInformation("=== Cache Eviction Demonstration ===");

        // Set a value with short expiration
        _cache.Set("temp:data", "This will expire soon", TimeSpan.FromMilliseconds(1000));

        _logger.LogInformation("Stored temporary data with 1 second expiration");

        // Wait for expiration
        await Task.Delay(1200);

        // Try to access expired data
        var found = _cache.TryGetValue("temp:data", out var value);

        _logger.LogInformation("Tried to get expired data: Found = {Found}", found);

        await Task.Delay(500);
    }

    private async Task DemonstrateGetOrCreate()
    {
        _logger.LogInformation("=== GetOrCreate Demonstration ===");

        // Use GetOrCreate extension method (if available)
        // First call will create the value
        var result1 = await _cache.GetOrCreateAsync("product:456", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);

            _logger.LogInformation("Factory method called - creating product data");

            // Simulate database call
            await Task.Delay(100);

            return new { Id = 456, Name = "Widget", Price = 19.99m };
        });

        _logger.LogInformation("First GetOrCreate call result: {Result}", result1);

        // Second call will hit the cache
        var result2 = await _cache.GetOrCreateAsync("product:456", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);

            _logger.LogInformation("Factory method called - this shouldn't happen on cache hit");

            await Task.Delay(100);

            return new { Id = 456, Name = "Widget", Price = 19.99m };
        });

        _logger.LogInformation("Second GetOrCreate call result: {Result}", result2);

        await Task.Delay(500);
    }
}

/// <summary>
/// Extension methods for IMemoryCache to provide GetOrCreate functionality.
/// These methods automatically work with MeteredMemoryCache and will be instrumented.
/// </summary>
public static class MemoryCacheExtensions
{
    /// <summary>
    /// Gets the value associated with the key if it exists, or creates and caches the value using the factory.
    /// </summary>
    public static async Task<T> GetOrCreateAsync<T>(this IMemoryCache cache, string key,
        Func<ICacheEntry, Task<T>> factory)
    {
        if (cache.TryGetValue(key, out var existingValue) && existingValue is T typedValue)
        {
            return typedValue;
        }

        using var entry = cache.CreateEntry(key);
        var value = await factory(entry);
        entry.Value = value;

        return value;
    }

    /// <summary>
    /// Gets the value associated with the key if it exists, or creates and caches the value using the factory.
    /// </summary>
    public static T GetOrCreate<T>(this IMemoryCache cache, string key,
        Func<ICacheEntry, T> factory)
    {
        if (cache.TryGetValue(key, out var existingValue) && existingValue is T typedValue)
        {
            return typedValue;
        }

        using var entry = cache.CreateEntry(key);
        var value = factory(entry);
        entry.Value = value;

        return value;
    }
}
