# k6 Performance Tests for ASP.NET Core MeteredMemoryCache Example

This directory contains comprehensive k6 performance tests for the ASP.NET Core example application with MeteredMemoryCache integration. These tests cover various performance scenarios including smoke tests, load tests, stress tests, soak tests, spike tests, and breakpoint tests.

## Test Files Overview

### 1. `k6-config.js`

Shared configuration and utility functions for all k6 tests:

- Custom metrics for cache performance monitoring
- Test configuration and data
- HTTP request options
- Utility functions for API endpoints
- Performance thresholds
- Test scenarios definitions

### 2. `k6-smoke-tests.js`

Basic functionality verification tests:

- **Duration**: 1 minute
- **Load**: 1 VU (Virtual User)
- **Purpose**: Verify all endpoints work correctly
- **Coverage**: All API endpoints, cache behavior, error handling

### 3. `k6-average-load-tests.js`

Normal usage pattern simulation:

- **Duration**: 5 minutes
- **Load**: 10 VUs
- **Purpose**: Simulate realistic user behavior
- **Coverage**: Mixed read/write operations, cache hit/miss patterns

### 4. `k6-stress-tests.js`

Breaking point identification:

- **Duration**: 9 minutes
- **Load**: 1 → 20 VUs (ramping)
- **Purpose**: Find system limits and breaking points
- **Coverage**: Aggressive load patterns, cache stress testing

### 5. `k6-soak-tests.js`

Memory leaks and stability testing:

- **Duration**: 30 minutes
- **Load**: 5 VUs (constant)
- **Purpose**: Identify memory leaks and stability issues
- **Coverage**: Sustained load, cache pressure, long-term stability

### 6. `k6-spike-tests.js`

Sudden traffic spike simulation:

- **Duration**: 4 minutes
- **Load**: 10 → 50 → 10 VUs (spike pattern)
- **Purpose**: Test system resilience to traffic spikes
- **Coverage**: Spike handling, recovery patterns, burst operations

### 7. `k6-breakpoint-tests.js`

Capacity planning and limits:

- **Duration**: 10 minutes
- **Load**: 10 → 20 → 30 → 40 → 50 VUs (gradual increase)
- **Purpose**: Identify capacity limits and breaking points
- **Coverage**: Gradual load increase, capacity planning, performance degradation

## Prerequisites

1. **k6 installed** (version 0.40.0 or later)

   ```bash
   # Install k6
   # Windows (using Chocolatey)
   choco install k6

   # macOS (using Homebrew)
   brew install k6

   # Linux
   sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
   echo "deb https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
   sudo apt-get update
   sudo apt-get install k6
   ```

2. **ASP.NET Core application running**

   ```bash
   cd examples/AspNetCore
   dotnet run
   ```

3. **Verify application is accessible**
   - Health endpoint: https://localhost:64494/health
   - Metrics endpoint: https://localhost:64494/metrics

## Running the Tests

### Individual Test Execution

```bash
# Smoke tests (1 minute)
k6 run k6-smoke-tests.js

# Average load tests (5 minutes)
k6 run k6-average-load-tests.js

# Stress tests (9 minutes)
k6 run k6-stress-tests.js

# Soak tests (30 minutes)
k6 run k6-soak-tests.js

# Spike tests (4 minutes)
k6 run k6-spike-tests.js

# Breakpoint tests (10 minutes)
k6 run k6-breakpoint-tests.js
```

### Custom Configuration

```bash
# Run with custom base URL
k6 run -e BASE_URL=https://your-api.com k6-smoke-tests.js

# Run with custom VUs and duration
k6 run --vus 20 --duration 10m k6-average-load-tests.js

# Run with custom thresholds
k6 run --threshold http_req_duration=p(95)<500 k6-stress-tests.js
```

### Test Suite Execution

```bash
# Run all tests in sequence
k6 run k6-smoke-tests.js && \
k6 run k6-average-load-tests.js && \
k6 run k6-stress-tests.js && \
k6 run k6-spike-tests.js && \
k6 run k6-breakpoint-tests.js

# Run soak tests separately (long duration)
k6 run k6-soak-tests.js
```

## Test Scenarios and Patterns

### Smoke Tests

- **Health checks** and basic endpoint verification
- **Cache behavior** testing (hit/miss patterns)
- **Error handling** verification
- **Metrics collection** validation

### Average Load Tests

- **Realistic user behavior** simulation
- **Mixed read/write operations** (70% reads, 30% writes)
- **Cache hit/miss patterns** under normal load
- **Batch operations** and cache management

### Stress Tests

- **Gradual load increase** to find breaking points
- **Aggressive cache testing** under stress
- **Burst operations** and rapid requests
- **System resilience** testing

### Soak Tests

- **Sustained load** over extended periods
- **Memory leak detection** through long-term monitoring
- **Cache pressure** and eviction testing
- **System stability** verification

### Spike Tests

- **Traffic spike simulation** (10 → 50 → 10 VUs)
- **Recovery pattern** testing
- **Burst operation** handling
- **System resilience** to sudden load changes

### Breakpoint Tests

- **Gradual load increase** (10 → 20 → 30 → 40 → 50 VUs)
- **Capacity limit identification**
- **Performance degradation** analysis
- **Breaking point** determination

## Metrics and Monitoring

### Custom Metrics

- **cache_hit_rate** - Cache hit percentage
- **cache_miss_rate** - Cache miss percentage
- **cache_eviction_rate** - Cache eviction rate
- **response_time** - Overall response time
- **cache_response_time** - Cache hit response time
- **api_response_time** - API call response time
- **error_rate** - Error percentage
- **request_count** - Total request count

### Performance Thresholds

- **Response time**: p(95) < 1000ms
- **Error rate**: < 5%
- **Cache hit rate**: > 80%
- **Cache response time**: p(95) < 100ms

### Monitoring Endpoints

- **Health**: `/health`
- **Metrics**: `/metrics` (Prometheus format)
- **Cache Stats**: `/api/cache/stats`

## Test Data and Configuration

### Test Data

- **User IDs**: 1-10
- **Product IDs**: 100-109
- **Category IDs**: 1-10
- **Search Queries**: laptop, phone, tablet, monitor, keyboard, mouse, headphones, speaker, camera, printer
- **Cache Names**: user-profiles, product-catalog, session-data, api-responses

### Environment Variables

- **BASE_URL**: Application base URL (default: https://localhost:64494)
- **HTTP_HOST_URL**: HTTP host URL (default: http://localhost:64495)

## Analysis and Reporting

### Key Performance Indicators

1. **Response Time Trends**

   - Monitor p(95) response times across different load levels
   - Identify performance degradation points
   - Compare cache hit vs miss response times

2. **Error Rate Analysis**

   - Track error rates under different load conditions
   - Identify error patterns and thresholds
   - Monitor system recovery after errors

3. **Cache Performance**

   - Monitor cache hit/miss ratios
   - Track cache eviction rates
   - Analyze cache response times

4. **System Stability**
   - Monitor memory usage over time (soak tests)
   - Track performance consistency
   - Identify memory leaks or resource issues

### Test Results Interpretation

#### Smoke Tests

- ✅ **Pass**: All endpoints respond correctly
- ❌ **Fail**: Basic functionality issues

#### Average Load Tests

- ✅ **Pass**: System handles normal load efficiently
- ❌ **Fail**: Performance issues under normal load

#### Stress Tests

- ✅ **Pass**: System handles stress gracefully
- ❌ **Fail**: System breaks under stress

#### Soak Tests

- ✅ **Pass**: No memory leaks, stable performance
- ❌ **Fail**: Memory leaks or performance degradation

#### Spike Tests

- ✅ **Pass**: System recovers from traffic spikes
- ❌ **Fail**: System fails to handle spikes

#### Breakpoint Tests

- ✅ **Pass**: Clear capacity limits identified
- ❌ **Fail**: Unclear breaking points

## Troubleshooting

### Common Issues

1. **Connection Refused**

   - Ensure ASP.NET Core application is running
   - Check port configuration in `launchSettings.json`
   - Verify firewall settings

2. **High Error Rates**

   - Check application logs for errors
   - Verify database connections
   - Monitor system resources

3. **Poor Cache Performance**

   - Check cache configuration
   - Monitor cache hit/miss ratios
   - Verify cache eviction policies

4. **Memory Issues**
   - Monitor memory usage during soak tests
   - Check for memory leaks in application
   - Verify garbage collection patterns

### Debug Tips

1. **Enable Verbose Logging**

   ```bash
   k6 run --verbose k6-smoke-tests.js
   ```

2. **Check Application Logs**

   - Monitor console output for cache operations
   - Look for OpenTelemetry metrics output
   - Check for error messages

3. **Monitor System Resources**

   - CPU usage during tests
   - Memory consumption over time
   - Network I/O patterns

4. **Analyze Metrics**
   - Check `/metrics` endpoint for detailed metrics
   - Monitor cache statistics via `/api/cache/stats`
   - Review OpenTelemetry traces

## Integration with CI/CD

### GitHub Actions Example

```yaml
name: k6 Performance Tests
on: [push, pull_request]
jobs:
  performance-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Install k6
        run: |
          sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
          echo "deb https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
          sudo apt-get update
          sudo apt-get install k6
      - name: Start Application
        run: |
          cd examples/AspNetCore
          dotnet run &
          sleep 30
      - name: Run Smoke Tests
        run: k6 run examples/AspNetCore/k6-smoke-tests.js
      - name: Run Load Tests
        run: k6 run examples/AspNetCore/k6-average-load-tests.js
```

### Jenkins Pipeline Example

```groovy
pipeline {
    agent any
    stages {
        stage('Start Application') {
            steps {
                sh 'cd examples/AspNetCore && dotnet run &'
                sleep 30
            }
        }
        stage('Performance Tests') {
            parallel {
                stage('Smoke Tests') {
                    steps {
                        sh 'k6 run examples/AspNetCore/k6-smoke-tests.js'
                    }
                }
                stage('Load Tests') {
                    steps {
                        sh 'k6 run examples/AspNetCore/k6-average-load-tests.js'
                    }
                }
            }
        }
    }
}
```

## Best Practices

1. **Test Environment**

   - Use production-like environment for accurate results
   - Ensure consistent test data
   - Monitor system resources during tests

2. **Test Execution**

   - Run tests in sequence: smoke → load → stress → spike → breakpoint
   - Run soak tests separately due to long duration
   - Monitor results and adjust thresholds as needed

3. **Analysis**

   - Compare results across different test runs
   - Monitor trends over time
   - Set up alerts for performance degradation

4. **Maintenance**
   - Update test data regularly
   - Adjust thresholds based on application changes
   - Review and update test scenarios

## Related Documentation

- [k6 Documentation](https://k6.io/docs/)
- [MeteredMemoryCache Documentation](../../docs/MeteredMemoryCache.md)
- [OpenTelemetry Integration](../../docs/OpenTelemetryIntegration.md)
- [Performance Characteristics](../../docs/PerformanceCharacteristics.md)
- [ASP.NET Core Example](../AspNetCore/Program.cs)
