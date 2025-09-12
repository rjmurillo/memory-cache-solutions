using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OpenTelemetry.Metrics;
using CacheImplementations;
using System.ComponentModel.DataAnnotations;

namespace AspNetCore;

/// <summary>
/// ASP.NET Core example demonstrating MeteredMemoryCache integration.
/// This example shows how to:
/// - Configure MeteredMemoryCache in ASP.NET Core applications
/// - Use multiple named caches in controllers and services
/// - Integrate with OpenTelemetry for metrics export
/// - Implement caching patterns in web APIs
/// - Monitor cache performance in production scenarios
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure services
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // Configure pipeline
        ConfigurePipeline(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add controllers
        services.AddControllers();

        // Add API documentation
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Configure OpenTelemetry
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter("AspNetCore.Cache") // Our application meter
                .AddAspNetCoreInstrumentation() // ASP.NET Core metrics
                .AddRuntimeInstrumentation() // .NET runtime metrics
                .AddHttpClientInstrumentation() // HTTP client metrics
                .AddConsoleExporter() // For demo purposes
                .AddPrometheusExporter()); // Production-ready metrics

        // Configure multiple named caches
        ConfigureCaches(services);

        // Register application services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICacheStatsService, CacheStatsService>();

        // Configure HTTP clients (for external API calls)
        services.AddHttpClient<IExternalApiService, ExternalApiService>(client =>
        {
            client.BaseAddress = new Uri("https://api.example.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Add health checks
        services.AddHealthChecks();
    }

    private static void ConfigureCaches(IServiceCollection services)
    {
        // User profile cache - frequently accessed, medium size
        services.AddNamedMeteredMemoryCache("user-profiles",
            meterName: "AspNetCore.Cache",
            configureOptions: options =>
            {
                options.AdditionalTags["cache_type"] = "user_profiles";
                options.AdditionalTags["environment"] = "development";
            });

        // Product catalog cache - less frequent updates, larger size
        services.AddNamedMeteredMemoryCache("product-catalog",
            meterName: "AspNetCore.Cache",
            configureOptions: options =>
            {
                options.AdditionalTags["cache_type"] = "product_catalog";
                options.AdditionalTags["environment"] = "development";
            });

        // Session data cache - small, frequent evictions
        services.AddNamedMeteredMemoryCache("session-data",
            meterName: "AspNetCore.Cache",
            configureOptions: options =>
            {
                options.AdditionalTags["cache_type"] = "session_data";
                options.AdditionalTags["environment"] = "development";
            });

        // API response cache - for external API responses
        services.AddNamedMeteredMemoryCache("api-responses",
            meterName: "AspNetCore.Cache",
            configureOptions: options =>
            {
                options.AdditionalTags["cache_type"] = "api_responses";
                options.AdditionalTags["environment"] = "development";
            });
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        // Configure the HTTP request pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthorization();

        // Add Prometheus metrics endpoint
        app.UseOpenTelemetryPrometheusScrapingEndpoint();

        // Add health check endpoint
        app.MapHealthChecks("/health");

        // Map controllers
        app.MapControllers();

        // Add a simple metrics endpoint for demo
        app.MapGet("/metrics-demo", () =>
        {
            return Results.Ok(new { message = "Check /metrics for Prometheus metrics" });
        });
    }
}

/// <summary>
/// Controller demonstrating cache usage patterns in web APIs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a user by ID with caching.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        try
        {
            var user = await _userService.GetUserAsync(id);
            return user == null ? NotFound() : Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Gets multiple users with batch caching optimization.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers([FromQuery] int[] ids)
    {
        if (!ids.Any())
            return BadRequest("At least one user ID is required");

        try
        {
            var users = await _userService.GetUsersAsync(ids);
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users {UserIds}", string.Join(",", ids));
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Updates a user and invalidates cache.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto updateDto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var updated = await _userService.UpdateUserAsync(id, updateDto);
            return updated ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}

/// <summary>
/// Controller for product catalog operations with caching.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductService productService, ILogger<ProductsController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a product by ID with caching.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        try
        {
            var product = await _productService.GetProductAsync(id);
            return product == null ? NotFound() : Ok(product);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving product {ProductId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Gets products by category with caching.
    /// </summary>
    [HttpGet("category/{categoryId:int}")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsByCategory(int categoryId)
    {
        try
        {
            var products = await _productService.GetProductsByCategoryAsync(categoryId);
            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving products for category {CategoryId}", categoryId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Searches products with cached results.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> SearchProducts([FromQuery] string query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Search query is required");

        try
        {
            var products = await _productService.SearchProductsAsync(query, page, pageSize);
            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products with query '{Query}'", query);
            return StatusCode(500, "Internal server error");
        }
    }
}

/// <summary>
/// Controller for cache statistics and monitoring.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CacheController : ControllerBase
{
    private readonly ICacheStatsService _cacheStatsService;

    public CacheController(ICacheStatsService cacheStatsService)
    {
        _cacheStatsService = cacheStatsService;
    }

    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    [HttpGet("stats")]
    public ActionResult<CacheStatsDto> GetStats()
    {
        var stats = _cacheStatsService.GetCacheStats();
        return Ok(stats);
    }

    /// <summary>
    /// Clears specific cache by name.
    /// </summary>
    [HttpDelete("{cacheName}")]
    public ActionResult ClearCache(string cacheName)
    {
        var cleared = _cacheStatsService.ClearCache(cacheName);
        return cleared ? NoContent() : NotFound($"Cache '{cacheName}' not found");
    }
}

/// <summary>
/// User service with caching patterns.
/// </summary>
public interface IUserService
{
    Task<UserDto?> GetUserAsync(int id);
    Task<IEnumerable<UserDto>> GetUsersAsync(int[] ids);
    Task<bool> UpdateUserAsync(int id, UpdateUserDto updateDto);
}

public class UserService : IUserService
{
    private readonly IMemoryCache _cache;
    private readonly IExternalApiService _externalApi;
    private readonly ILogger<UserService> _logger;

    public UserService(
        [FromKeyedServices("user-profiles")] IMemoryCache cache,
        IExternalApiService externalApi,
        ILogger<UserService> logger)
    {
        _cache = cache;
        _externalApi = externalApi;
        _logger = logger;
    }

    public async Task<UserDto?> GetUserAsync(int id)
    {
        var cacheKey = $"user:{id}";

        if (_cache.TryGetValue(cacheKey, out UserDto? cachedUser) && cachedUser is not null)
        {
            _logger.LogDebug("User {UserId} found in cache", id);
            return cachedUser;
        }

        _logger.LogDebug("User {UserId} not in cache, fetching from API", id);

        var user = await _externalApi.GetUserAsync(id);

        if (user != null)
        {
            _cache.Set(cacheKey, user, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(15),
                Priority = CacheItemPriority.Normal,
                Size = 1
            });
        }

        return user;
    }

    public async Task<IEnumerable<UserDto>> GetUsersAsync(int[] ids)
    {
        var users = new List<UserDto>();
        var uncachedIds = new List<int>();

        // Check cache for each user
        foreach (var id in ids)
        {
            var cacheKey = $"user:{id}";
            if (_cache.TryGetValue(cacheKey, out UserDto? cachedUser) && cachedUser is not null)
            {
                users.Add(cachedUser);
                _logger.LogDebug("User {UserId} found in cache", id);
            }
            else
            {
                uncachedIds.Add(id);
            }
        }

        // Batch fetch uncached users
        if (uncachedIds.Any())
        {
            _logger.LogDebug("Fetching {Count} users from API: {UserIds}",
                uncachedIds.Count, string.Join(",", uncachedIds));

            var fetchedUsers = await _externalApi.GetUsersAsync(uncachedIds.ToArray());

            foreach (var user in fetchedUsers)
            {
                var cacheKey = $"user:{user.Id}";
                _cache.Set(cacheKey, user, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(15),
                    Priority = CacheItemPriority.Normal,
                    Size = 1
                });

                users.Add(user);
            }
        }

        return users;
    }

    public async Task<bool> UpdateUserAsync(int id, UpdateUserDto updateDto)
    {
        var updated = await _externalApi.UpdateUserAsync(id, updateDto);

        if (updated)
        {
            // Invalidate cache after update
            var cacheKey = $"user:{id}";
            _cache.Remove(cacheKey);
            _logger.LogDebug("Invalidated cache for updated user {UserId}", id);
        }

        return updated;
    }
}

/// <summary>
/// Product service with caching patterns.
/// </summary>
public interface IProductService
{
    Task<ProductDto?> GetProductAsync(int id);
    Task<IEnumerable<ProductDto>> GetProductsByCategoryAsync(int categoryId);
    Task<IEnumerable<ProductDto>> SearchProductsAsync(string query, int page, int pageSize);
}

public class ProductService : IProductService
{
    private readonly IMemoryCache _cache;
    private readonly IExternalApiService _externalApi;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        [FromKeyedServices("product-catalog")] IMemoryCache cache,
        IExternalApiService externalApi,
        ILogger<ProductService> logger)
    {
        _cache = cache;
        _externalApi = externalApi;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(int id)
    {
        var cacheKey = $"product:{id}";

        if (_cache.TryGetValue(cacheKey, out ProductDto? cachedProduct) && cachedProduct is not null)
        {
            return cachedProduct;
        }

        var product = await _externalApi.GetProductAsync(id);

        if (product != null)
        {
            _cache.Set(cacheKey, product, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
                Priority = CacheItemPriority.High, // Products are important
                Size = 2
            });
        }

        return product;
    }

    public async Task<IEnumerable<ProductDto>> GetProductsByCategoryAsync(int categoryId)
    {
        var cacheKey = $"products:category:{categoryId}";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<ProductDto>? cachedProducts) && cachedProducts is not null)
        {
            return cachedProducts;
        }

        var products = await _externalApi.GetProductsByCategoryAsync(categoryId);

        _cache.Set(cacheKey, products, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
            Priority = CacheItemPriority.Normal,
            Size = 10 // Category lists are larger
        });

        return products;
    }

    public async Task<IEnumerable<ProductDto>> SearchProductsAsync(string query, int page, int pageSize)
    {
        // Cache search results with query, page, and pageSize in key
        var cacheKey = $"search:products:{query.ToLowerInvariant()}:{page}:{pageSize}";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<ProductDto>? cachedResults) && cachedResults is not null)
        {
            return cachedResults;
        }

        var results = await _externalApi.SearchProductsAsync(query, page, pageSize);

        _cache.Set(cacheKey, results, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), // Search results expire quickly
            Priority = CacheItemPriority.Low, // Search results can be evicted
            Size = 5
        });

        return results;
    }
}

/// <summary>
/// External API service (simulated).
/// </summary>
public interface IExternalApiService
{
    Task<UserDto?> GetUserAsync(int id);
    Task<IEnumerable<UserDto>> GetUsersAsync(int[] ids);
    Task<bool> UpdateUserAsync(int id, UpdateUserDto updateDto);
    Task<ProductDto?> GetProductAsync(int id);
    Task<IEnumerable<ProductDto>> GetProductsByCategoryAsync(int categoryId);
    Task<IEnumerable<ProductDto>> SearchProductsAsync(string query, int page, int pageSize);
}

public class ExternalApiService : IExternalApiService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExternalApiService> _logger;

    public ExternalApiService(
        HttpClient httpClient,
        [FromKeyedServices("api-responses")] IMemoryCache cache,
        ILogger<ExternalApiService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<UserDto?> GetUserAsync(int id)
    {
        // Simulate API call delay
        await Task.Delay(Random.Shared.Next(100, 300));

        return new UserDto
        {
            Id = id,
            Name = $"User {id}",
            Email = $"user{id}@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 365))
        };
    }

    public async Task<IEnumerable<UserDto>> GetUsersAsync(int[] ids)
    {
        // Simulate batch API call
        await Task.Delay(Random.Shared.Next(200, 500));

        return ids.Select(id => new UserDto
        {
            Id = id,
            Name = $"User {id}",
            Email = $"user{id}@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 365))
        });
    }

    public async Task<bool> UpdateUserAsync(int id, UpdateUserDto updateDto)
    {
        // Simulate API update call
        await Task.Delay(Random.Shared.Next(150, 400));
        return true; // Assume success
    }

    public async Task<ProductDto?> GetProductAsync(int id)
    {
        await Task.Delay(Random.Shared.Next(100, 300));

        return new ProductDto
        {
            Id = id,
            Name = $"Product {id}",
            Price = Random.Shared.Next(10, 1000),
            CategoryId = Random.Shared.Next(1, 10),
            Description = $"Description for product {id}"
        };
    }

    public async Task<IEnumerable<ProductDto>> GetProductsByCategoryAsync(int categoryId)
    {
        await Task.Delay(Random.Shared.Next(200, 500));

        return Enumerable.Range(1, 10).Select(i => new ProductDto
        {
            Id = categoryId * 100 + i,
            Name = $"Product {categoryId}-{i}",
            Price = Random.Shared.Next(10, 1000),
            CategoryId = categoryId,
            Description = $"Product in category {categoryId}"
        });
    }

    public async Task<IEnumerable<ProductDto>> SearchProductsAsync(string query, int page, int pageSize)
    {
        await Task.Delay(Random.Shared.Next(300, 800)); // Search is slower

        var skip = (page - 1) * pageSize;

        return Enumerable.Range(skip + 1, pageSize).Select(i => new ProductDto
        {
            Id = i,
            Name = $"Product {i} matching '{query}'",
            Price = Random.Shared.Next(10, 1000),
            CategoryId = Random.Shared.Next(1, 10),
            Description = $"Search result for '{query}'"
        });
    }
}

/// <summary>
/// Cache statistics service for monitoring.
/// </summary>
public interface ICacheStatsService
{
    CacheStatsDto GetCacheStats();
    bool ClearCache(string cacheName);
}

public class CacheStatsService : ICacheStatsService
{
    private readonly IServiceProvider _serviceProvider;

    public CacheStatsService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public CacheStatsDto GetCacheStats()
    {
        // In a real implementation, you'd collect actual metrics
        // This is simplified for demo purposes
        return new CacheStatsDto
        {
            Caches = new[]
            {
                new CacheInfoDto { Name = "user-profiles", EstimatedSize = 1500, MaxSize = 5000 },
                new CacheInfoDto { Name = "product-catalog", EstimatedSize = 3200, MaxSize = 10000 },
                new CacheInfoDto { Name = "session-data", EstimatedSize = 450, MaxSize = 1000 },
                new CacheInfoDto { Name = "api-responses", EstimatedSize = 800, MaxSize = 2000 }
            },
            Timestamp = DateTime.UtcNow
        };
    }

    public bool ClearCache(string cacheName)
    {
        try
        {
            var cache = _serviceProvider.GetKeyedService<IMemoryCache>(cacheName);

            if (cache is MemoryCache memCache)
            {
                // Note: MemoryCache doesn't have a public Clear method
                // In production, you might need to track keys separately or use a custom cache
                return true; // Simulate success
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

// DTOs
public record UserDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record UpdateUserDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;
}

public record ProductDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int CategoryId { get; init; }
    public string Description { get; init; } = string.Empty;
}

public record CacheStatsDto
{
    public IEnumerable<CacheInfoDto> Caches { get; init; } = Array.Empty<CacheInfoDto>();
    public DateTime Timestamp { get; init; }
}

public record CacheInfoDto
{
    public string Name { get; init; } = string.Empty;
    public int EstimatedSize { get; init; }
    public int MaxSize { get; init; }
}
