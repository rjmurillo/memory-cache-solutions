# ASP.NET Core MeteredMemoryCache Example

This example demonstrates how to use the `MeteredMemoryCache` in an ASP.NET Core application with OpenTelemetry integration for comprehensive metrics and monitoring.

## Directory Structure

```text
examples/AspNetCore/
├── src/                    # ASP.NET Core application source code
│   ├── AspNetCore.csproj   # Project file
│   ├── Program.cs          # Main application code
│   └── Properties/         # Application properties
│       └── launchSettings.json
├── tests/                  # Performance and integration tests
│   ├── k6-*.js            # k6 performance test scripts
│   ├── *.http             # HTTP test files for API testing
│   ├── run-k6-tests.*     # Cross-platform test runners
│   ├── package.json       # npm scripts for test execution
│   └── http-client.env.json
├── docs/                  # Documentation
│   ├── README-HTTP-Tests.md
│   └── README-k6-Tests.md
└── README.md              # This file
```

## Quick Start

### 1. Start the Application

```bash
cd src
dotnet run
```

The application will start on:

- HTTPS: <https://localhost:64494>
- HTTP: <http://localhost:64495>

### 2. Test the Application

#### Using HTTP Files (Visual Studio/Rider)

- Open any `.http` file in the `tests/` directory
- Click "Send Request" to test individual endpoints

#### Using k6 Performance Tests

```bash
cd tests

# Install k6 (if not already installed)
# Windows: winget install k6
# macOS: brew install k6
# Linux: https://k6.io/docs/getting-started/installation/

# Run all tests
pwsh run-k6-tests.ps1

# Run specific test
pwsh run-k6-tests.ps1 -TestName smoke

# Using npm scripts
npm run test:smoke
npm run test:all
```

## Features Demonstrated

### ASP.NET Core Application (`src/`)

- **MeteredMemoryCache Integration**: Shows how to register and use named caches
- **OpenTelemetry Metrics**: Comprehensive metrics collection and export
- **RESTful API**: User and product management endpoints
- **Cache Management**: Cache statistics, clearing, and monitoring
- **Health Checks**: Application health monitoring
- **Swagger Documentation**: API documentation at `/swagger`

### Performance Testing (`tests/`)

- **k6 Load Tests**: Comprehensive performance testing suite
- **HTTP Test Files**: Manual API testing with Visual Studio/Rider
- **Cross-Platform Support**: PowerShell Core scripts for all platforms
- **Multiple Test Types**: Smoke, load, stress, soak, spike, and breakpoint tests

### Documentation (`docs/`)

- **HTTP Tests Guide**: How to use the HTTP test files
- **k6 Tests Guide**: Comprehensive k6 performance testing documentation

## API Endpoints

### Health & Metrics

- `GET /health` - Application health check
- `GET /metrics` - Prometheus metrics endpoint

### User Management

- `GET /api/users/{id}` - Get user by ID
- `GET /api/users?ids={ids}` - Get multiple users
- `PUT /api/users/{id}` - Update user

### Product Management

- `GET /api/products/{id}` - Get product by ID
- `GET /api/products/category/{categoryId}` - Get products by category
- `GET /api/products/search?query={query}` - Search products

### Cache Management

- `GET /api/cache/stats` - Get cache statistics
- `DELETE /api/cache/{cacheName}` - Clear specific cache

## Configuration

### Environment Variables

- `BASE_URL`: Application base URL (default: https://localhost:64494)
- `HTTP_HOST_URL`: HTTP host URL (default: http://localhost:64495)

### Cache Configuration

The application demonstrates multiple named caches:

- `user-profiles`: User data caching
- `product-catalog`: Product data caching
- `session-data`: Session information caching
- `api-responses`: API response caching

## Performance Testing

### Test Types

1. **Smoke Tests**: Basic functionality verification (1 minute, 1 VU)
2. **Average Load Tests**: Normal usage patterns (5 minutes, 10 VUs)
3. **Stress Tests**: Breaking point identification (9 minutes, 1-20 VUs)
4. **Soak Tests**: Memory leaks and stability (30 minutes, 5 VUs)
5. **Spike Tests**: Traffic spike simulation (4 minutes, 10-50-10 VUs)
6. **Breakpoint Tests**: Capacity planning (10 minutes, 10-50 VUs)

### Running Tests

```bash
# Cross-platform PowerShell Core (recommended)
pwsh run-k6-tests.ps1

# Platform-specific wrappers
bash run-k6-tests.sh        # Linux/macOS
run-k6-tests.bat            # Windows

# Using npm scripts
npm run test:all
npm run test:smoke
npm run test:load
```

## Metrics and Monitoring

### OpenTelemetry Metrics

- Cache hit/miss rates
- Response times
- Request counts
- Error rates
- Memory usage

### Prometheus Integration

Metrics are available at `/metrics` endpoint in Prometheus format for integration with monitoring systems.

## Development

### Prerequisites

- .NET 9.0 SDK
- k6 (for performance testing)
- PowerShell Core (for cross-platform scripts)

### Building

```bash
cd src
dotnet build
```

### Running

```bash
cd src
dotnet run
```

### Testing

```bash
cd tests
pwsh run-k6-tests.ps1 -TestName smoke
```

## Related Documentation

- [HTTP Tests Guide](docs/README-HTTP-Tests.md)
- [k6 Performance Tests Guide](docs/README-k6-Tests.md)
- [MeteredMemoryCache Documentation](../../docs/MeteredMemoryCache.md)
- [OpenTelemetry Integration](../../docs/OpenTelemetryIntegration.md)
- [Performance Characteristics](../../docs/PerformanceCharacteristics.md)
