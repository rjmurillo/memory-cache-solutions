# HTTP Test Files for ASP.NET Core MeteredMemoryCache Example

This directory contains comprehensive HTTP test files for testing the ASP.NET Core example application with MeteredMemoryCache integration. These files follow the [Microsoft .http file format](https://learn.microsoft.com/en-us/aspnet/core/test/http-files?view=aspnetcore-9.0) and can be used in Visual Studio 2022 or other compatible tools.

## Files Overview

### 1. `http-client.env.json`

Environment configuration file that defines different environments (dev, production) with their respective host addresses and shared variables.

### 2. `AspNetCore.http`

Main test file containing comprehensive tests for all API endpoints:

- Health checks and metrics endpoints
- User API endpoints (GET, PUT)
- Product API endpoints (GET by ID, category, search)
- Cache management endpoints
- Basic performance and concurrency tests
- Error handling scenarios

### 3. `CacheTests.http`

Specialized cache testing scenarios:

- Cache hit/miss verification
- Cache invalidation testing
- Manual cache clearing
- Cache metrics verification
- Cache size and eviction testing
- Cache expiration testing
- Cache priority testing
- Cache tag verification
- Cache concurrency testing

### 4. `PerformanceTests.http`

Performance and concurrency testing:

- Cache warm-up procedures
- Cache hit vs miss performance comparison
- Concurrent access testing
- Mixed concurrent access patterns
- Batch operation performance
- Cache pressure testing
- Metrics performance testing
- Stress testing scenarios

### 5. `ErrorHandlingTests.http`

Comprehensive error handling and edge case testing:

- Invalid input validation
- Not found error scenarios
- Request body validation
- Query parameter validation
- HTTP method validation
- Header validation
- Cache error handling
- Edge cases and boundary conditions
- Concurrent error handling
- Application resilience testing

## Prerequisites

1. **Visual Studio 2022** (version 17.8 or later) with the **ASP.NET and web development** workload
2. **Running ASP.NET Core application** on the configured ports (default: <https://localhost:64494>)

## Setup Instructions

1. **Start the ASP.NET Core application:**

   ```bash
   cd examples/AspNetCore
   dotnet run
   ```

2. **Verify the application is running:**

   - Open <https://localhost:64494/health> in your browser
   - You should see a healthy response

3. **Open the HTTP test files in Visual Studio 2022:**

   - The `.http` files will be recognized automatically
   - Environment selector will appear in the top-right corner

## Usage Instructions

### Running Individual Tests

1. **Select Environment:**

   - Use the environment dropdown in the top-right corner
   - Choose "dev" for local development testing

2. **Run Single Request:**

   - Click the "Send Request" link above any request
   - View the response in the right pane

3. **Run All Requests in a File:**

   - Use Ctrl+A to select all requests
   - Right-click and select "Send All Requests"

### Testing Scenarios

#### Basic Functionality Testing

Start with `AspNetCore.http` to verify basic API functionality:

- Health checks
- User CRUD operations
- Product catalog operations
- Cache management

#### Cache Behavior Testing

Use `CacheTests.http` to verify cache-specific behavior:

- Cache hit/miss patterns
- Cache invalidation
- Cache metrics emission
- Cache eviction policies

#### Performance Testing

Use `PerformanceTests.http` to test performance characteristics:

- Cache hit vs miss performance
- Concurrent access patterns
- Stress testing scenarios

#### Error Handling Testing

Use `ErrorHandlingTests.http` to verify error handling:

- Input validation
- Error responses
- Application resilience

## Key Testing Areas

### 1. Cache Metrics Verification

The tests verify that MeteredMemoryCache properly emits metrics:

- `cache_hits_total` - Number of cache hits
- `cache_misses_total` - Number of cache misses
- `cache_evictions_total` - Number of cache evictions
- Cache name tags (`cache.name`)
- Additional tags (`cache_type`, `priority`)

### 2. Multiple Cache Testing

Tests verify four different named caches:

- `user-profiles` - High priority, 15-minute sliding expiration
- `product-catalog` - High priority, 1-hour absolute expiration
- `session-data` - High priority, frequent evictions
- `api-responses` - Low priority, external API responses

### 3. Cache Invalidation Testing

Tests verify cache invalidation works correctly:

- User updates invalidate user cache
- Manual cache clearing via API
- Cache eviction under pressure

### 4. Performance Characteristics

Tests measure and verify:

- Cache hit performance (should be fast)
- Cache miss performance (includes API simulation delay)
- Concurrent access thread safety
- Metrics collection performance

### 5. Error Handling

Tests verify proper error handling:

- Invalid input validation
- Not found scenarios
- Malformed requests
- Application resilience under error conditions

## Environment Configuration

The `http-client.env.json` file supports multiple environments:

```json
{
  "dev": {
    "HostAddress": "https://localhost:64494",
    "HttpHostAddress": "http://localhost:64495"
  },
  "production": {
    "HostAddress": "https://api.example.com",
    "HttpHostAddress": "http://api.example.com"
  },
  "$shared": {
    "ApiVersion": "v1",
    "ContentType": "application/json",
    "Accept": "application/json"
  }
}
```

## Variables Used

The test files use various variables for flexibility:

- `@userId`, `@productId`, `@categoryId` - Test data IDs
- `@searchQuery`, `@page`, `@pageSize` - Search parameters
- `@cacheNames` - Cache name list
- `@concurrentUsers`, `@concurrentProducts` - Concurrency test data

## Expected Results

### Successful Responses

- **200 OK** - Successful GET requests
- **204 No Content** - Successful PUT/DELETE requests
- **404 Not Found** - Non-existent resources
- **400 Bad Request** - Invalid input

### Cache Behavior

- **First request** - Cache miss (slower response)
- **Subsequent requests** - Cache hit (faster response)
- **After invalidation** - Cache miss (slower response)

### Metrics

- Prometheus metrics at `/metrics` endpoint
- Cache statistics at `/api/cache/stats` endpoint
- OpenTelemetry console output (if configured)

## Troubleshooting

### Common Issues

1. **Connection Refused**

   - Ensure the ASP.NET Core application is running
   - Check the port configuration in `launchSettings.json`

2. **404 Errors**

   - Verify the application is running in Development mode
   - Check that all controllers are properly registered

3. **Cache Not Working**

   - Verify MeteredMemoryCache is properly configured
   - Check OpenTelemetry metrics configuration

4. **Environment Not Loading**

   - Close and reopen the `.http` file
   - Press F6 to refresh the environment selector

### Debug Tips

1. **Check Application Logs**

   - Monitor console output for cache operations
   - Look for OpenTelemetry metrics output

2. **Verify Metrics**

   - Check `/metrics` endpoint for cache metrics
   - Verify cache name tags are present

3. **Test Cache Behavior**

   - Use the same request multiple times to verify caching
   - Check response times (cache hits should be faster)

## Integration with CI/CD

These HTTP test files can be integrated into CI/CD pipelines using tools like:

- **Newman** (Postman CLI) with HTTP file support
- **REST Client** extensions for various IDEs
- **Custom scripts** that parse and execute HTTP requests

## Contributing

When adding new tests:

1. Follow the existing naming conventions
2. Add appropriate comments explaining the test purpose
3. Include both positive and negative test cases
4. Update this README if adding new test categories
5. Ensure tests are environment-agnostic using variables

## Related Documentation

- [Microsoft .http Files Documentation](https://learn.microsoft.com/en-us/aspnet/core/test/http-files?view=aspnetcore-9.0)
- [MeteredMemoryCache Documentation](../../docs/MeteredMemoryCache.md)
- [OpenTelemetry Integration](../../docs/OpenTelemetryIntegration.md)
- [Performance Characteristics](../../docs/PerformanceCharacteristics.md)
