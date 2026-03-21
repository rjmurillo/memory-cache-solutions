# Container Cache Sizing Guide

> **Audience:** Engineers deploying .NET applications with `IMemoryCache` in containers (Docker, Kubernetes, ACI, etc.)
>
> **Related:** [Best Practices](BestPractices.md) · [Performance Characteristics](PerformanceCharacteristics.md) · [OpenTelemetry Integration](OpenTelemetryIntegration.md)

## Why Containers Need Explicit Cache Budgets

`System.Runtime.Caching.MemoryCache` ties eviction to **host-wide physical memory pressure** via [`CacheMemoryLimit`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache.cachememorylimit) and [`PhysicalMemoryLimit`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache.physicalmemorylimit). In containers, those heuristics often misrepresent the cgroup memory limit, producing two failure modes:

1. **Premature evictions** — GC reports high memory load under a tight container limit, triggering aggressive scavenging before the cache is actually full.
2. **Late eviction → OOM kill** — the cache doesn't see pressure until the container exceeds its cgroup limit, and the orchestrator kills the process.

`Microsoft.Extensions.Caching.Memory.MemoryCache` avoids this by design: it does **not** inspect physical RAM. Instead, you opt into predictability with a logical [`SizeLimit`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.memorycacheoptions.sizelimit) and per-entry `Size` values. Units are arbitrary — you define what "1" means (one entry, one kilobyte, one cost unit).

> [!IMPORTANT]
>
> Once a cache has a `SizeLimit`, **every entry must set `Size`** or `Set` throws `InvalidOperationException`. This is why you should **not** put `SizeLimit` on the shared `services.AddMemoryCache()` instance. Create a **dedicated sized cache** for bounded scenarios.

## Sizing Strategy

### Step 1: Determine Your Container Memory Budget

Start from the container's memory limit and work backward:

```
Container memory limit (e.g., 512 MiB)
  − .NET runtime overhead (GC heap, JIT, thread stacks, etc.)
  − Application working set (request buffers, serialization, etc.)
  − Safety margin (10–20%)
  ────────────────────────────────
  = Available for cache(s)
```

**Rough heuristics** (starting points, not prescriptions):

| Container Limit | Typical Cache Budget | Rationale                                              |
| --------------- | -------------------- | ------------------------------------------------------ |
| 256 MiB         | 30–60 MiB            | Tight; prefer time-based expiry over size caps         |
| 512 MiB         | 80–150 MiB           | Room for a modest bounded cache                        |
| 1 GiB+          | 200–400 MiB          | Comfortable; size limits optional unless data is large |

These are approximations. Profile your application under realistic load to find the right balance.

### Step 2: Translate Memory Budget to Logical SizeLimit

Choose a size unit and convert:

**Option A: 1 unit = 1 entry** (simplest, works when entries are roughly uniform)

```csharp
// ~5,000 DTOs averaging 10 KB each ≈ 50 MiB cache footprint
services.AddNamedMeteredMemoryCache("orders", options =>
{
    options.SizeLimit = 5_000;
    options.CompactionPercentage = 0.25;
});

// Every entry uses Size = 1
cache.Set(key, order, new MemoryCacheEntryOptions
{
    Size = 1,
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
});
```

**Option B: 1 unit = 1 KiB** (better when entry sizes vary widely)

```csharp
// 80 MiB budget → 81,920 units
services.AddNamedMeteredMemoryCache("documents", options =>
{
    options.SizeLimit = 80 * 1024;
    options.CompactionPercentage = 0.10; // aggressive cleanup for large objects
});

// Estimate entry size
int estimatedKiB = (serializedBytes.Length + 256 + 1023) / 1024; // ceiling division; +256 for overhead
cache.Set(key, document, new MemoryCacheEntryOptions
{
    Size = Math.Max(1, estimatedKiB),
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
});
```

> [!TIP]
>
> If you can't easily estimate sizes, start with **Option A** (count-based) and a conservative limit. Measure cache eviction metrics in production and adjust. A wrong-but-present limit is better than no limit in a container.

### Step 3: Keep the Shared Cache Unbounded

The default `services.AddMemoryCache()` cache is shared across the application. **Do not set `SizeLimit` on it** — any component that forgets to set `Size` on an entry will throw at runtime.

Instead, create **dedicated caches** for bounded scenarios:

```csharp
// Shared cache — unbounded, time-based expiry only
services.AddMemoryCache(options =>
{
    options.TrackStatistics = true;
});

// Bounded cache for large objects — separate instance with size limit
services.AddNamedMeteredMemoryCache("image-thumbnails", options =>
{
    options.SizeLimit = 500;
    options.CompactionPercentage = 0.20;
    options.CacheName = "image-thumbnails";
});
```

This pattern keeps the shared cache safe for general use while giving you predictable eviction on the bounded cache.

## Time-Based Expiry vs Size Limits

For most containerized workloads, **time-based expiry alone is sufficient** and avoids the `Size` bookkeeping entirely:

```csharp
services.AddMemoryCache(options =>
{
    options.TrackStatistics = true;
    // No SizeLimit — rely on expiration to control memory
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
});

cache.Set(key, value, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
    SlidingExpiration = TimeSpan.FromMinutes(2)
});
```

Use size limits only when:

- Entries are large or vary widely in size (images, documents, serialized blobs)
- You need a hard cap to protect against unbounded growth
- Time-based expiry alone doesn't prevent memory pressure under your workload

## Monitoring Cache Behavior in Containers

Instrument your caches so you can **observe** whether sizing is correct, rather than guessing:

```csharp
services.AddNamedMeteredMemoryCache("my-cache", options =>
{
    options.CacheName = "my-cache";
});

// Wire up OpenTelemetry to export metrics
services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Microsoft.Extensions.Caching.Memory.MemoryCache");
        // Add your exporter (OTLP, Prometheus, etc.)
    });
```

**Key metrics to watch:**

| Metric                                       | What It Tells You                                                                                                                                                                           |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `cache.evictions`                            | High sustained evictions suggest the cache is too small or TTLs are too short                                                                                                               |
| `cache.requests` (`cache.request.type=miss`) | High miss rate means the cache isn't saving enough work                                                                                                                                     |
| `cache.entries`                              | Entry count; trend over time shows growth patterns, and a flat line near your chosen capacity suggests the cache is effectively "full"                                                      |
| `cache.estimated_size`                       | Sum of entry `Size` values in your chosen unit (from `CurrentEstimatedSize`); compare to `SizeLimit` directly since both use the same user-defined unit (requires `TrackStatistics = true`) |

**Container-specific signals to correlate:**

- **Container memory usage** (from cAdvisor, Kubernetes metrics-server, or `docker stats`) — compare against your cache budget assumptions
- **GC pause time and Gen2 collections** — rising GC pressure may indicate the cache is consuming too much of the container's memory
- **OOM kill events** — if you see these, your total memory footprint (including cache) exceeds the container limit

## Example: Kubernetes Deployment

> [!IMPORTANT]
>
> For memory-sensitive workloads (in-memory caches), set `requests.memory` **equal to** `limits.memory` to place the pod in the [**Guaranteed** QoS class](https://kubernetes.io/docs/concepts/workloads/pods/pod-qos/) — the last to be evicted under node memory pressure. If `requests < limits` (Burstable QoS), the scheduler may place the pod on a node with only the _requested_ amount available, and the pod will OOM the moment it exceeds that — even though its _limit_ is higher. See [Robusta: Stop Using CPU Limits on Kubernetes](https://home.robusta.dev/blog/kubernetes-memory-limit/) for a detailed explanation of why memory is different from CPU.

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: my-service
spec:
  template:
    spec:
      containers:
        - name: my-service
          resources:
            requests:
              memory: "512Mi"
            limits:
              memory: "512Mi" # Equal to request → Guaranteed QoS
          env:
            - name: OTEL_EXPORTER_OTLP_ENDPOINT
              value: "http://otel-collector:4317"
            - name: OTEL_SERVICE_NAME
              value: "my-service"
```

With a 512 MiB limit, a reasonable starting cache configuration:

```csharp
// ~100 MiB for cached data, leaving ~400 MiB for runtime + app
services.AddNamedMeteredMemoryCache("api-responses", options =>
{
    options.SizeLimit = 100 * 1024; // 100 MiB in KiB units
    options.CompactionPercentage = 0.25;
    options.CacheName = "api-responses";
});
```

## Common Mistakes

### ❌ Setting SizeLimit on the shared cache

```csharp
// DON'T — any component that forgets Size will throw
services.AddMemoryCache(options =>
{
    options.SizeLimit = 10000; // ← breaks third-party code that uses IMemoryCache
});
```

### ❌ Assuming SizeLimit units are bytes

```csharp
// DON'T — SizeLimit has no inherent unit; this doesn't mean "100 bytes"
options.SizeLimit = 100;
// Each entry's Size is in whatever unit YOU define
```

### ❌ Ignoring cache metrics in containers

```csharp
// DON'T — without metrics, you're flying blind
services.AddMemoryCache(options =>
{
    // No TrackStatistics = true or OpenTelemetry meter added
});
```

Without metrics, you can't tell whether evictions are from expiration (normal) or capacity pressure (potential problem). Always enable `TrackStatistics = true` and emit OTel metrics.

### ❌ Copying SizeLimit from host-based deployments

A cache that worked fine on a 16 GiB VM may OOM a 512 MiB container. Re-derive your cache budget from the container's memory limit, not the host's.

## Further Reading

- [Best Practices — Memory Size Configuration](BestPractices.md#memory-size-configuration) — size calculation guidelines by object type
- [OpenTelemetry Integration](OpenTelemetryIntegration.md) — full metrics pipeline setup
- [Performance Characteristics](PerformanceCharacteristics.md) — benchmark data for cache overhead
- [`MemoryCacheOptions.SizeLimit` docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.memorycacheoptions.sizelimit) — official API reference
- [dotnet/runtime#124140](https://github.com/dotnet/runtime/issues/124140) — native `MemoryCache` metrics API (approved for .NET 11)
