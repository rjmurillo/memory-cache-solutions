# OpenTelemetry Integration Guide for MeteredMemoryCache

## Overview

MeteredMemoryCache integrates seamlessly with OpenTelemetry to provide standardized cache metrics. This guide covers setup and configuration for various OpenTelemetry exporters and deployment scenarios.

## Quick Start

### Basic Console Setup

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using CacheImplementations;

var builder = Host.CreateApplicationBuilder(args);

// Register OpenTelemetry with console exporter
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")           // Match your meter name
        .AddConsoleExporter());            // Output to console

// Register MeteredMemoryCache
builder.Services.AddNamedMeteredMemoryCache("user-cache",
    meterName: "MyApp.Cache");

var app = builder.Build();
await app.RunAsync();
```

## Exporter Configurations

### 1. Prometheus Exporter

**Best for**: Kubernetes environments, Grafana dashboards, production monitoring

```csharp
// Install: OpenTelemetry.Exporter.Prometheus.AspNetCore
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddPrometheusExporter());

// ASP.NET Core: Add endpoint
app.MapPrometheusScrapingEndpoint(); // Default: /metrics
```

**Configuration Options:**

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddPrometheusExporter(options =>
        {
            options.ScrapeEndpointPath = "/cache-metrics";
            options.ScrapeResponseCacheDurationMilliseconds = 5000;
        }));
```

**Sample Prometheus Output:**

```prometheus
# HELP cache_hits_total Number of cache hits
# TYPE cache_hits_total counter
cache_hits_total{cache_name="user-cache"} 1547

# HELP cache_misses_total Number of cache misses
# TYPE cache_misses_total counter
cache_misses_total{cache_name="user-cache"} 423

# HELP cache_evictions_total Number of cache evictions
# TYPE cache_evictions_total counter
cache_evictions_total{cache_name="user-cache",reason="Capacity"} 89
```

### 2. OTLP Exporter (OpenTelemetry Protocol)

**Best for**: Cloud-native environments, OpenTelemetry Collector, vendor-agnostic setup

```csharp
// Install: OpenTelemetry.Exporter.OpenTelemetryProtocol
using OpenTelemetry.Exporter;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://otel-collector:4317");
            options.Protocol = OtlpExportProtocol.Grpc;
            options.Headers = "api-key=your-api-key";
        }));
```

**HTTP Protocol:**

```csharp
.AddOtlpExporter(options =>
{
    options.Endpoint = new Uri("https://api.honeycomb.io/v1/metrics");
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    options.Headers = "x-honeycomb-team=your-api-key";
});
```

**Environment Variables:**

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
export OTEL_EXPORTER_OTLP_HEADERS="api-key=your-key"
export OTEL_EXPORTER_OTLP_PROTOCOL="grpc"
```

### 3. Azure Monitor Exporter

**Best for**: Azure-hosted applications, Application Insights integration

```csharp
// Install: Azure.Monitor.OpenTelemetry
using Azure.Monitor.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache"))
    .UseAzureMonitor(options =>
    {
        options.ConnectionString = "InstrumentationKey=your-key;IngestionEndpoint=https://...";
    });
```

**App Settings Configuration:**

```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=your-key;IngestionEndpoint=https://...",
  "OTEL_RESOURCE_ATTRIBUTES": "service.name=my-app,service.version=1.0.0"
}
```

### 4. AWS CloudWatch Exporter

**Best for**: AWS-hosted applications, CloudWatch integration

```csharp
// Install: OpenTelemetry.Contrib.Exporter.AWSCloudWatch
using OpenTelemetry.Contrib.Exporter.AWSCloudWatch;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddAWSCloudWatchMetrics(options =>
        {
            options.Region = "us-west-2";
            options.Namespace = "MyApp/Cache";
        }));
```

### 5. Google Cloud Monitoring

**Best for**: Google Cloud Platform deployments

```csharp
// Install: Google.Cloud.Diagnostics.AspNetCore
using Google.Cloud.Diagnostics.AspNetCore;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddGoogleCloudMonitoring(options =>
        {
            options.ProjectId = "your-project-id";
        }));
```

### 6. DataDog Exporter

**Best for**: DataDog monitoring platform

```csharp
// Install: OpenTelemetry.Exporter.DataDog
using OpenTelemetry.Exporter.DataDog;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddDataDogExporter(options =>
        {
            options.AgentHost = "datadog-agent";
            options.AgentPort = 8125;
            options.ApiKey = "your-api-key";
        }));
```

### 7. Jaeger Exporter

**Best for**: Local development, distributed tracing environments

```csharp
// Install: OpenTelemetry.Exporter.Jaeger (deprecated, use OTLP)
// Recommended: Use OTLP exporter to Jaeger
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://jaeger:14250");
            options.Protocol = OtlpExportProtocol.Grpc;
        }));
```

## Advanced Configurations

### Multiple Exporters

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddConsoleExporter()           // Development
        .AddPrometheusExporter()        // Production monitoring
        .AddOtlpExporter());            // Centralized collection
```

### Resource Configuration

```csharp
using OpenTelemetry.Resources;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("my-cache-service", "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = "production",
            ["service.instance.id"] = Environment.MachineName,
            ["cache.implementation"] = "MeteredMemoryCache"
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddPrometheusExporter());
```

### Filtering and Sampling

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddView("cache_hits_total", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = new[] { 0, 5, 10, 25, 50, 75, 100, 250, 500, 1000 }
        })
        .AddPrometheusExporter());
```

### Periodic Export Configuration

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter(options =>
        {
            options.ExportProcessorType = ExportProcessorType.Batch;
            options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
            {
                MaxExportBatchSize = 512,
                ScheduledDelayMilliseconds = 5000,
                ExporterTimeoutMilliseconds = 30000,
                MaxQueueSize = 2048
            };
        }));
```

## Container Deployments

### Docker Compose with OpenTelemetry Collector

```yaml
version: "3.8"
services:
  app:
    build: .
    environment:
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
      - OTEL_RESOURCE_ATTRIBUTES=service.name=cache-app
    depends_on:
      - otel-collector

  otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - "4317:4317" # OTLP gRPC receiver
      - "8889:8889" # Prometheus metrics

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
```

**OpenTelemetry Collector Configuration:**

```yaml
# otel-collector-config.yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:

exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"
  logging:
    loglevel: debug

service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [prometheus, logging]
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: cache-app
spec:
  template:
    spec:
      containers:
        - name: app
          image: cache-app:latest
          env:
            - name: OTEL_EXPORTER_OTLP_ENDPOINT
              value: "http://otel-collector.observability:4317"
            - name: OTEL_RESOURCE_ATTRIBUTES
              value: "service.name=cache-app,k8s.namespace.name=$(NAMESPACE),k8s.pod.name=$(POD_NAME)"
            - name: NAMESPACE
              valueFrom:
                fieldRef:
                  fieldPath: metadata.namespace
            - name: POD_NAME
              valueFrom:
                fieldRef:
                  fieldPath: metadata.name
```

## Cloud Provider Specific Setups

### Azure Container Apps

```bicep
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'cache-app'
  properties: {
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
    }
    template: {
      containers: [
        {
          name: 'app'
          image: 'cache-app:latest'
          env: [
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationInsights.properties.ConnectionString
            }
            {
              name: 'OTEL_RESOURCE_ATTRIBUTES'
              value: 'service.name=cache-app,service.version=1.0.0'
            }
          ]
        }
      ]
    }
  }
}
```

### AWS ECS Task Definition

```json
{
  "family": "cache-app",
  "taskRoleArn": "arn:aws:iam::account:role/ECSTaskRole",
  "containerDefinitions": [
    {
      "name": "app",
      "image": "cache-app:latest",
      "environment": [
        {
          "name": "OTEL_EXPORTER_OTLP_ENDPOINT",
          "value": "http://otel-collector:4317"
        },
        {
          "name": "OTEL_RESOURCE_ATTRIBUTES",
          "value": "service.name=cache-app,cloud.provider=aws,cloud.region=us-west-2"
        }
      ]
    }
  ]
}
```

### Google Cloud Run

```yaml
apiVersion: serving.knative.dev/v1
kind: Service
metadata:
  name: cache-app
  annotations:
    run.googleapis.com/execution-environment: gen2
spec:
  template:
    metadata:
      annotations:
        autoscaling.knative.dev/maxScale: "100"
    spec:
      containers:
        - image: cache-app:latest
          env:
            - name: GOOGLE_CLOUD_PROJECT
              value: "your-project-id"
            - name: OTEL_RESOURCE_ATTRIBUTES
              value: "service.name=cache-app,cloud.provider=gcp,cloud.region=us-central1"
```

## Monitoring and Alerting

### Prometheus Alerting Rules

```yaml
# cache-alerts.yml
groups:
  - name: cache.rules
    rules:
      - alert: HighCacheMissRate
        expr: |
          (
            rate(cache_misses_total[5m]) / 
            (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))
          ) > 0.5
        for: 2m
        labels:
          severity: warning
        annotations:
          summary: "High cache miss rate detected"
          description: "Cache {{ $labels.cache_name }} has a miss rate of {{ $value | humanizePercentage }}"

      - alert: ExcessiveCacheEvictions
        expr: rate(cache_evictions_total[5m]) > 10
        for: 1m
        labels:
          severity: warning
        annotations:
          summary: "Excessive cache evictions"
          description: "Cache {{ $labels.cache_name }} is evicting {{ $value }} items per second"
```

### Grafana Dashboard Query Examples

```promql
# Cache Hit Rate
rate(cache_hits_total[5m]) /
(rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))

# Cache Operations Per Second
rate(cache_hits_total[5m]) + rate(cache_misses_total[5m])

# Eviction Rate by Reason
rate(cache_evictions_total[5m]) by (reason)

# Cache Efficiency (hits per operation)
increase(cache_hits_total[1h]) /
(increase(cache_hits_total[1h]) + increase(cache_misses_total[1h]))
```

## Troubleshooting

### No Metrics Appearing

1. **Verify Meter Name Match:**

```csharp
// Ensure meter name matches in both registration and OpenTelemetry config
var meter = new Meter("MyApp.Cache");
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("MyApp.Cache")); // Must match
```

2. **Check Resource Configuration:**

```csharp
// Add console exporter to debug
.AddConsoleExporter()
```

3. **Validate Export Endpoint:**

```bash
# Test OTLP endpoint
curl -v http://otel-collector:4317

# Test Prometheus endpoint
curl http://localhost:9090/metrics
```

### Missing Tags

```csharp
// Verify cache name is set
services.AddNamedMeteredMemoryCache("user-cache"); // Name required for tags

// Check additional tags
services.AddNamedMeteredMemoryCache("user-cache", opts =>
{
    opts.AdditionalTags["environment"] = "prod"; // Custom tags
});
```

### High Memory Usage

```csharp
// Configure export intervals
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddPeriodicExportingMetricReader(
            exporterFactory: (provider) => new ConsoleMetricExporter(),
            exportIntervalMilliseconds: 10000)); // 10 second intervals
```

### Performance Impact

```csharp
// Use batch processing for high-throughput scenarios
.AddOtlpExporter(options =>
{
    options.ExportProcessorType = ExportProcessorType.Batch;
    options.BatchExportProcessorOptions = new()
    {
        MaxExportBatchSize = 1024,
        ScheduledDelayMilliseconds = 1000
    };
});
```

## Best Practices

### 1. Resource Labeling

```csharp
.ConfigureResource(resource => resource
    .AddService("cache-service", "1.0.0")
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = Environment.GetEnvironmentVariable("ENVIRONMENT"),
        ["service.instance.id"] = Environment.MachineName,
        ["service.namespace"] = "cache"
    }));
```

### 2. Consistent Naming

```csharp
// Use hierarchical meter names
var cacheMetrics = new Meter("MyCompany.MyApp.Cache");
var databaseMetrics = new Meter("MyCompany.MyApp.Database");
```

### 3. Environment-Specific Configuration

```csharp
if (builder.Environment.IsDevelopment())
{
    // Development: Console + OTLP to local collector
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddMeter("MyApp.Cache")
            .AddConsoleExporter()
            .AddOtlpExporter());
}
else
{
    // Production: Cloud provider specific
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddMeter("MyApp.Cache")
            .AddPrometheusExporter());
}
```

### 4. Security Considerations

```csharp
// Use managed identity for cloud providers
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddAzureMonitor()); // Uses managed identity automatically

// Secure API keys
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyApp.Cache")
        .AddOtlpExporter(options =>
        {
            options.Headers = $"api-key={builder.Configuration["OpenTelemetry:ApiKey"]}";
        }));
```

## Performance Recommendations

### Export Frequency

- **Development**: 5-10 seconds for quick feedback
- **Production**: 30-60 seconds to reduce overhead
- **High-throughput**: 60+ seconds with larger batch sizes

### Resource Limits

```csharp
.AddOtlpExporter(options =>
{
    options.BatchExportProcessorOptions = new()
    {
        MaxExportBatchSize = 512,        // Adjust based on memory constraints
        MaxQueueSize = 2048,             // Prevent unbounded growth
        ScheduledDelayMilliseconds = 30000, // 30 second intervals
        ExporterTimeoutMilliseconds = 30000
    };
});
```

### Memory Management

- Monitor telemetry pipeline memory usage
- Use appropriate batch sizes for your workload
- Consider sampling for extremely high-volume scenarios

## Related Documentation

- [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md)
- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/instrumentation/net/)
- [Prometheus Export Format](https://prometheus.io/docs/instrumenting/exposition_formats/)
- [OTLP Specification](https://opentelemetry.io/docs/reference/specification/protocol/)
