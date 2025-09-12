using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;
using CacheImplementations;

namespace MultiCache;

/// <summary>
/// Multi-cache example demonstrating multiple named MeteredMemoryCache instances.
/// This example shows how to:
/// - Configure multiple named cache instances with different configurations
/// - Use dimensional metrics to distinguish between cache instances
/// - Implement cache hierarchies (L1/L2 patterns)
/// - Monitor performance across different cache types
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("MeteredMemoryCache Multi-Cache Example");
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
                .AddMeter("MultiCache.Example") // Our meter name
                .AddConsoleExporter());

        // Register multiple named cache instances
        RegisterCaches(builder.Services);

        // Register our demo services
        builder.Services.AddTransient<UserService>();
        builder.Services.AddTransient<ProductService>();
        builder.Services.AddTransient<SessionService>();
        builder.Services.AddTransient<MultiCacheDemo>();

        // Build and run
        using var host = builder.Build();
        
        var demo = host.Services.GetRequiredService<MultiCacheDemo>();
        
        await demo.RunDemoAsync();
        
        Console.WriteLine("\nDemo completed. Press any key to exit...");
        Console.ReadKey();
    }

    private static void RegisterCaches(IServiceCollection services)
    {
        // User cache - optimized for frequent access, larger size
        services.AddKeyedSingleton<IMemoryCache>("user-cache", (provider, key) =>
        {
            var meter = new Meter("MultiCache.Example");
            var innerCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 10000, // Large cache for users
                CompactionPercentage = 0.1
            });
            return new MeteredMemoryCache(innerCache, meter, "user-cache");
        });

        // Product cache - medium size, longer TTL
        services.AddKeyedSingleton<IMemoryCache>("product-cache", (provider, key) =>
        {
            var meter = new Meter("MultiCache.Example");
            var innerCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 5000,
                CompactionPercentage = 0.2
            });
            return new MeteredMemoryCache(innerCache, meter, "product-cache");
        });

        // Session cache - smaller, shorter TTL, frequent evictions
        services.AddKeyedSingleton<IMemoryCache>("session-cache", (provider, key) =>
        {
            var meter = new Meter("MultiCache.Example");
            var innerCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 1000, // Small cache for sessions
                CompactionPercentage = 0.3
            });
            return new MeteredMemoryCache(innerCache, meter, "session-cache");
        });

        // L1 cache for hierarchical pattern
        services.AddKeyedSingleton<IMemoryCache>("l1-cache", (provider, key) =>
        {
            var meter = new Meter("MultiCache.Example");
            var innerCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 100, // Very small L1 cache
                CompactionPercentage = 0.1
            });
            return new MeteredMemoryCache(innerCache, meter, "l1-cache");
        });
    }
}

/// <summary>
/// Service demonstrating user-specific caching patterns.
/// </summary>
public class UserService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserService> _logger;

    public UserService([FromKeyedServices("user-cache")] IMemoryCache cache, ILogger<UserService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<User> GetUserAsync(int userId)
    {
        var cacheKey = $"user:{userId}";
        
        if (_cache.TryGetValue(cacheKey, out var cachedUser) && cachedUser is User user)
        {
            _logger.LogInformation("User {UserId} found in cache", userId);
            return user;
        }

        _logger.LogInformation("User {UserId} not in cache, loading from database", userId);
        
        // Simulate database load
        await Task.Delay(100);
        
        var newUser = new User
        { 
            Id = userId, 
            Name = $"User {userId}", 
            Email = $"user{userId}@example.com",
            LastLogin = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30))
        };

        // Cache with sliding expiration
        _cache.Set(cacheKey, newUser, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15),
            Size = 1 // Each user counts as 1 unit
        });

        return newUser;
    }
}

/// <summary>
/// Service demonstrating product catalog caching.
/// </summary>
public class ProductService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProductService> _logger;

    public ProductService([FromKeyedServices("product-cache")] IMemoryCache cache, ILogger<ProductService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<Product> GetProductAsync(int productId)
    {
        var cacheKey = $"product:{productId}";
        
        if (_cache.TryGetValue(cacheKey, out var cachedProduct) && cachedProduct is Product product)
        {
            _logger.LogInformation("Product {ProductId} found in cache", productId);
            return product;
        }

        _logger.LogInformation("Product {ProductId} not in cache, loading from catalog", productId);
        
        // Simulate catalog service call
        await Task.Delay(200);
        
        var newProduct = new Product
        { 
            Id = productId, 
            Name = $"Product {productId}", 
            Price = Random.Shared.Next(10, 1000),
            Description = $"Description for product {productId}",
            CategoryId = Random.Shared.Next(1, 10)
        };

        // Cache with absolute expiration (product data changes less frequently)
        _cache.Set(cacheKey, newProduct, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
            Priority = CacheItemPriority.High, // Products are important to keep
            Size = 2 // Products are larger than users
        });

        return newProduct;
    }

    public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(int categoryId)
    {
        var cacheKey = $"products:category:{categoryId}";
        
        if (_cache.TryGetValue(cacheKey, out var cachedProducts) && cachedProducts is IEnumerable<Product> products)
        {
            _logger.LogInformation("Products for category {CategoryId} found in cache", categoryId);
            return products;
        }

        _logger.LogInformation("Products for category {CategoryId} not in cache, loading from catalog", categoryId);
        
        // Simulate loading multiple products
        await Task.Delay(300);
        
        var newProducts = Enumerable.Range(1, 5).Select(i => new Product
        {
            Id = categoryId * 100 + i,
            Name = $"Product {categoryId}-{i}",
            Price = Random.Shared.Next(10, 1000),
            CategoryId = categoryId
        }).ToArray();

        // Cache with priority and larger size
        _cache.Set(cacheKey, newProducts, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
            Priority = CacheItemPriority.Normal,
            Size = 10 // Category lists are expensive
        });

        return newProducts;
    }
}

/// <summary>
/// Service demonstrating session caching with frequent evictions.
/// </summary>
public class SessionService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<SessionService> _logger;

    public SessionService([FromKeyedServices("session-cache")] IMemoryCache cache, ILogger<SessionService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<UserSession> GetSessionAsync(string sessionId)
    {
        var cacheKey = $"session:{sessionId}";
        
        if (_cache.TryGetValue(cacheKey, out var cachedSession) && cachedSession is UserSession session)
        {
            _logger.LogInformation("Session {SessionId} found in cache", sessionId);
            
            // Update last accessed time
            session.LastAccessed = DateTime.UtcNow;
            
            // Re-cache with new timestamp
            _cache.Set(cacheKey, session, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5), // Short session timeout
                Size = 1
            });
            
            return session;
        }

        _logger.LogInformation("Session {SessionId} not in cache, creating new session", sessionId);
        
        // Simulate session creation
        await Task.Delay(50);
        
        var newSession = new UserSession
        { 
            SessionId = sessionId,
            UserId = Random.Shared.Next(1, 1000),
            CreatedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            Data = new Dictionary<string, object>
            {
                ["theme"] = "dark",
                ["language"] = "en-US",
                ["cart_items"] = Random.Shared.Next(0, 10)
            }
        };

        _cache.Set(cacheKey, newSession, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
            Priority = CacheItemPriority.Low, // Sessions can be evicted easily
            Size = 1
        });

        return newSession;
    }
}

/// <summary>
/// Main demo orchestrator that exercises all cache types.
/// </summary>
public class MultiCacheDemo
{
    private readonly UserService _userService;
    private readonly ProductService _productService;
    private readonly SessionService _sessionService;
    private readonly IMemoryCache _l1Cache;
    private readonly ILogger<MultiCacheDemo> _logger;

    public MultiCacheDemo(
        UserService userService,
        ProductService productService,
        SessionService sessionService,
        [FromKeyedServices("l1-cache")] IMemoryCache l1Cache,
        ILogger<MultiCacheDemo> logger)
    {
        _userService = userService;
        _productService = productService;
        _sessionService = sessionService;
        _l1Cache = l1Cache;
        _logger = logger;
    }

    public async Task RunDemoAsync()
    {
        _logger.LogInformation("Starting multi-cache demonstration...");

        // Demonstrate different cache patterns
        await DemonstrateUserCaching();
        await DemonstrateProductCaching();
        await DemonstrateSessionCaching();
        await DemonstrateHierarchicalCaching();
        await DemonstrateEvictionPatterns();
        
        _logger.LogInformation("Multi-cache demonstration completed.");
        
        // Wait for metrics to be exported
        await Task.Delay(5000);
    }

    private async Task DemonstrateUserCaching()
    {
        _logger.LogInformation("=== User Cache Demonstration ===");
        
        // Load some users (cache misses)
        for (int i = 1; i <= 5; i++)
        {
            await _userService.GetUserAsync(i);
            await Task.Delay(100);
        }

        // Access same users again (cache hits)
        for (int i = 1; i <= 5; i++)
        {
            await _userService.GetUserAsync(i);
            await Task.Delay(50);
        }

        _logger.LogInformation("User cache demonstration completed");
    }

    private async Task DemonstrateProductCaching()
    {
        _logger.LogInformation("=== Product Cache Demonstration ===");
        
        // Load individual products
        for (int i = 1; i <= 3; i++)
        {
            await _productService.GetProductAsync(i);
            await Task.Delay(100);
        }

        // Load products by category (larger cache entries)
        for (int categoryId = 1; categoryId <= 3; categoryId++)
        {
            await _productService.GetProductsByCategoryAsync(categoryId);
            await Task.Delay(150);
        }

        // Access cached data again
        await _productService.GetProductAsync(1);
        await _productService.GetProductsByCategoryAsync(1);

        _logger.LogInformation("Product cache demonstration completed");
    }

    private async Task DemonstrateSessionCaching()
    {
        _logger.LogInformation("=== Session Cache Demonstration ===");
        
        var sessionIds = new[] { "sess_001", "sess_002", "sess_003", "sess_004", "sess_005" };
        
        // Create sessions
        foreach (var sessionId in sessionIds)
        {
            await _sessionService.GetSessionAsync(sessionId);
            await Task.Delay(100);
        }

        // Access sessions multiple times (sliding expiration renewal)
        for (int round = 0; round < 3; round++)
        {
            foreach (var sessionId in sessionIds)
            {
                await _sessionService.GetSessionAsync(sessionId);
                await Task.Delay(50);
            }
        }

        _logger.LogInformation("Session cache demonstration completed");
    }

    private async Task DemonstrateHierarchicalCaching()
    {
        _logger.LogInformation("=== Hierarchical Cache (L1) Demonstration ===");
        
        // Use L1 cache as a small, fast cache for frequently accessed data
        var frequentKeys = new[] { "hot_data_1", "hot_data_2", "hot_data_3" };
        
        foreach (var key in frequentKeys)
        {
            if (!_l1Cache.TryGetValue(key, out var value))
            {
                _logger.LogInformation("L1 miss for {Key}, simulating L2 lookup", key);
                
                // Simulate L2 cache lookup or database call
                await Task.Delay(200);
                
                value = $"Expensive data for {key}";
                
                _l1Cache.Set(key, value, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                    Priority = CacheItemPriority.High,
                    Size = 5 // Expensive data takes more space
                });
            }
            else
            {
                _logger.LogInformation("L1 hit for {Key}", key);
            }
        }

        // Access the same data again (should be L1 hits)
        foreach (var key in frequentKeys)
        {
            _l1Cache.TryGetValue(key, out var value);
            _logger.LogInformation("L1 access for {Key}: {Value}", key, value);
        }

        _logger.LogInformation("Hierarchical cache demonstration completed");
    }

    private async Task DemonstrateEvictionPatterns()
    {
        _logger.LogInformation("=== Cache Eviction Demonstration ===");
        
        // Fill up the session cache to trigger evictions
        _logger.LogInformation("Filling session cache to trigger evictions...");
        
        for (int i = 1; i <= 50; i++)
        {
            await _sessionService.GetSessionAsync($"temp_session_{i:D3}");
            
            if (i % 10 == 0)
            {
                _logger.LogInformation("Created {SessionCount} temporary sessions", i);
                await Task.Delay(100);
            }
        }

        // Create high-priority data in L1 cache and then overflow it
        _logger.LogInformation("Overflowing L1 cache to trigger evictions...");
        
        for (int i = 1; i <= 30; i++)
        {
            var key = $"overflow_data_{i:D2}";
            var value = $"Data batch {i}";
            
            _l1Cache.Set(key, value, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                Priority = i <= 15 ? CacheItemPriority.High : CacheItemPriority.Low,
                Size = 3
            });
            
            if (i % 5 == 0)
            {
                await Task.Delay(100);
            }
        }

        _logger.LogInformation("Eviction demonstration completed");
    }
}

/// <summary>
/// User data model.
/// </summary>
public record User
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTime LastLogin { get; init; }
}

/// <summary>
/// Product data model.
/// </summary>
public record Product
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Description { get; init; } = string.Empty;
    public int CategoryId { get; init; }
}

/// <summary>
/// User session data model.
/// </summary>
public record UserSession
{
    public string SessionId { get; init; } = string.Empty;
    public int UserId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastAccessed { get; set; }
    public Dictionary<string, object> Data { get; init; } = new();
}
