---
title: Built-in MemoryCache metrics in .NET 11 (and when you still need MeteredMemoryCache)
description: MemoryCache emits dotnet.cache.* OpenTelemetry metrics in .NET 11. Enable TrackStatistics, name caches, and use MeteredMemoryCache on .NET 8 to 10.
---

# Built-in MemoryCache metrics in .NET 11 (and when you still need MeteredMemoryCache)

Shipping in .NET 11 (available in previews/RC), `MemoryCache` publishes four OpenTelemetry metrics on the `Microsoft.Extensions.Caching.Memory.MemoryCache` meter: `dotnet.cache.requests`, `dotnet.cache.evictions`, `dotnet.cache.entries`, and `dotnet.cache.estimated_size`. They appear only when `TrackStatistics = true`. On .NET 8 to 10, use [MeteredMemoryCache](MeteredMemoryCache.md).

Everything on this page was checked against `Microsoft.Extensions.Caching.Memory` `11.0.0-rc.1.26425.128` on the .NET SDK `11.0.100-rc.1.26425.128`. Names, units, and tags come from a `MeterListener` dump of that package. The feature came from [dotnet/runtime#126146](https://github.com/dotnet/runtime/pull/126146), authored by Richard Murillo, which implements the API proposal [dotnet/runtime#124140](https://github.com/dotnet/runtime/issues/124140).

## Table of Contents

- [The four instruments](#the-four-instruments)
- [Turn them on](#turn-them-on)
- [Name each cache](#name-each-cache)
- [Without DI](#without-di)
- [See the metrics locally](#see-the-metrics-locally)
- [TotalEvictions in GetCurrentStatistics()](#totalevictions-in-getcurrentstatistics)
- [When you still need MeteredMemoryCache](#when-you-still-need-meteredmemorycache)
- [Migrating from MeteredMemoryCache to built-in metrics](#migrating-from-meteredmemorycache-to-built-in-metrics)
- [FAQ](#faq)
- [See also](#see-also)

## The four instruments

Meter name: `Microsoft.Extensions.Caching.Memory.MemoryCache`.

| Name                          | Type                            | Unit         | Tags                                             | Meaning                                                                                     |
| ----------------------------- | ------------------------------- | ------------ | ------------------------------------------------ | ------------------------------------------------------------------------------------------- |
| `dotnet.cache.requests`       | `ObservableCounter<long>`       | `{request}`  | `dotnet.cache.name`, `dotnet.cache.request.type` | Total cache requests. `dotnet.cache.request.type` is `hit` or `miss`.                       |
| `dotnet.cache.evictions`      | `ObservableCounter<long>`       | `{eviction}` | `dotnet.cache.name`                              | Total cache evictions. Counts expiration, token, and capacity removals; not `Remove` calls. |
| `dotnet.cache.entries`        | `ObservableUpDownCounter<long>` | `{entry}`    | `dotnet.cache.name`                              | Current number of cache entries.                                                            |
| `dotnet.cache.estimated_size` | `ObservableGauge<long>`         | `By`         | `dotnet.cache.name`                              | Estimated size of the cache. Reports a value only when `SizeLimit` is set.                  |

The descriptions in the package are "Total cache requests.", "Total cache evictions.", "Current number of cache entries.", and "Estimated size of the cache.".

Notes from testing:

- **Hit ratio** is `hit / (hit + miss)` from `dotnet.cache.requests`.
- **Evictions**: a manual `Remove` and a replacement (`Set` on an existing key) did not increase `dotnet.cache.evictions`. Expiration, a cancelled change token, and `Compact` did.
- **Estimated size**: the instrument is created whenever `TrackStatistics = true`, but it only reports a measurement when `MemoryCacheOptions.SizeLimit` is set. Setting `Size` on entries without a `SizeLimit` still reports nothing. Once `SizeLimit` is set, every entry must set `Size`, or `Set` throws `InvalidOperationException`. The value is the sum of entry `Size` values, in whatever unit you assign them.
- **Cache name tag**: every measurement carries `dotnet.cache.name`. When the cache gets an `IMeterFactory`, the meter itself also carries a `dotnet.cache.name` tag, so OpenTelemetry shows it on the instrumentation scope.

## Turn them on

Set `TrackStatistics` and `Name` in `AddMemoryCache`, then add the meter to OpenTelemetry:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMemoryCache(options =>
{
    options.TrackStatistics = true; // required: no instruments without it
    options.Name = "products";      // becomes the dotnet.cache.name tag
});

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.Extensions.Caching.Memory.MemoryCache")
        .AddConsoleExporter());

using var host = builder.Build();
await host.StartAsync();

var cache = host.Services.GetRequiredService<IMemoryCache>();
cache.Set("sku-1", "Widget");
cache.TryGetValue("sku-1", out _); // hit
cache.TryGetValue("sku-2", out _); // miss

// Export now: a disposed MemoryCache reports no measurements.
host.Services.GetRequiredService<MeterProvider>().ForceFlush();
await host.StopAsync();
```

Packages: `Microsoft.Extensions.Caching.Memory` and `Microsoft.Extensions.Hosting` `11.0.0-rc.1.26425.128`, `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.Console` `1.19.1`. In a real app, swap the console exporter for OTLP, Prometheus, or Azure Monitor. The `ForceFlush` call is only there because this sample exits right away. The host disposes the cache before the exporter's final flush, and a disposed cache reports nothing.

The console exporter printed (trimmed, timestamps removed):

```text
Metric Name: dotnet.cache.requests, Description: Total cache requests., Unit: {request}, Metric Type: LongSum
Instrumentation scope (Meter):
	Name: Microsoft.Extensions.Caching.Memory.MemoryCache
	Tags:
		dotnet.cache.name: products
dotnet.cache.name: products dotnet.cache.request.type: hit
Value: 1
dotnet.cache.name: products dotnet.cache.request.type: miss
Value: 1

Metric Name: dotnet.cache.evictions, Description: Total cache evictions., Unit: {eviction}, Metric Type: LongSum
...
Metric Name: dotnet.cache.entries, Description: Current number of cache entries., Unit: {entry}, Metric Type: LongSumNonMonotonic
...
dotnet.cache.name: products
Value: 1
```

There is no `dotnet.cache.estimated_size` series because this cache has no `SizeLimit`.

**The `TrackStatistics` gate.** With `TrackStatistics = false` (the default), `MemoryCache` creates no instruments at all. A `MeterListener` saw zero instruments published, and `GetCurrentStatistics()` returns `null`. Adding the meter to OpenTelemetry is not enough on its own.

**How DI picks the constructor.** `AddMemoryCache()` is unchanged. The container picks the `MemoryCache(IOptions<MemoryCacheOptions>, ILoggerFactory, IMeterFactory)` constructor only when both `ILoggerFactory` and `IMeterFactory` are registered. `Host.CreateApplicationBuilder` registers both, which is why the output above shows `dotnet.cache.name` on the instrumentation scope. In a bare `ServiceCollection` with only `AddMetrics()`, the container picked the options-only constructor. The metrics still appeared, on the shared meter described in [Without DI](#without-di).

## Name each cache

`MemoryCacheOptions.Name` sets the `dotnet.cache.name` tag. The default is `"Default"`.

Two caches left at the default are hard to tell apart. In testing, two unnamed caches each reported their own series, both tagged `dotnet.cache.name=Default`. A backend that aggregates by tag merges them into one series, and you cannot tell which cache has the poor hit ratio. Give every cache a distinct, low-cardinality name such as `products` or `sessions`.

## Without DI

Pass an `IMeterFactory` to the new three-argument constructor. `loggerFactory` accepts `null`:

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
var meterFactory = services.GetRequiredService<IMeterFactory>();

using var products = new MemoryCache(
    Options.Create(new MemoryCacheOptions { TrackStatistics = true, Name = "products" }),
    loggerFactory: null,
    meterFactory);

// No IMeterFactory: the instruments go on a shared meter with the same name.
using var sessions = new MemoryCache(
    new MemoryCacheOptions { TrackStatistics = true, Name = "sessions" });

products.Set("sku-1", "Widget");
sessions.TryGetValue("user-1", out _);

Console.WriteLine($"products entries: {products.GetCurrentStatistics()!.CurrentEntryCount}");
Console.WriteLine($"sessions misses: {sessions.GetCurrentStatistics()!.TotalMisses}");
```

`AddMetrics()` comes from the `Microsoft.Extensions.Diagnostics` package. Without a factory, `MemoryCache` uses a shared meter named `Microsoft.Extensions.Caching.Memory.MemoryCache` that has no meter-level tags. Each measurement still carries `dotnet.cache.name`, so `AddMeter("Microsoft.Extensions.Caching.Memory.MemoryCache")` picks up both caches above.

## See the metrics locally

### dotnet-counters

```bash
dotnet-counters monitor -p <PID> --counters Microsoft.Extensions.Caching.Memory.MemoryCache
```

Tested with `dotnet-counters` `10.0.750501` against a `net11.0` process. Output (trimmed):

```text
[Microsoft.Extensions.Caching.Memory.MemoryCache]
    dotnet.cache.entries ({entry})
        dotnet.cache.name
        -----------------
        products                     1
    dotnet.cache.estimated_size (By)
        dotnet.cache.name
        -----------------
        products                     3
    dotnet.cache.evictions ({eviction})
        dotnet.cache.name
        -----------------
        products                     0
    dotnet.cache.requests ({request})
        dotnet.cache.name dotnet.cache.request.type
        ----------------- -------------------------
        products          hit                    304
        products          miss                   303
```

`dotnet-counters collect` with the same `--counters` value also works and writes CSV or JSON.

### MeterListener console repro

This program prints every measurement the cache reports:

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

using var cache = new MemoryCache(new MemoryCacheOptions
{
    TrackStatistics = true,
    Name = "products",
    SizeLimit = 1_000, // without it, dotnet.cache.estimated_size reports nothing
});

cache.Set("sku-1", "Widget", new MemoryCacheEntryOptions { Size = 1 });
cache.TryGetValue("sku-1", out _); // hit
cache.TryGetValue("sku-2", out _); // miss

using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == "Microsoft.Extensions.Caching.Memory.MemoryCache")
    {
        meterListener.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
{
    var tagText = string.Join(", ", tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
    Console.WriteLine($"{instrument.Name} ({instrument.Unit}) = {value} [{tagText}]");
});
listener.Start();
listener.RecordObservableInstruments();
```

Output:

```text
dotnet.cache.requests ({request}) = 1 [dotnet.cache.name=products, dotnet.cache.request.type=hit]
dotnet.cache.requests ({request}) = 1 [dotnet.cache.name=products, dotnet.cache.request.type=miss]
dotnet.cache.evictions ({eviction}) = 0 [dotnet.cache.name=products]
dotnet.cache.entries ({entry}) = 1 [dotnet.cache.name=products]
dotnet.cache.estimated_size (By) = 1 [dotnet.cache.name=products]
```

## TotalEvictions in GetCurrentStatistics()

`MemoryCacheStatistics` gains `TotalEvictions` (`long`), so you can read the same numbers without a metrics pipeline:

```csharp
using Microsoft.Extensions.Caching.Memory;

using var cache = new MemoryCache(new MemoryCacheOptions { TrackStatistics = true, Name = "products" });

cache.Set("a", 1);
cache.Set("b", 2);
cache.TryGetValue("a", out _); // hit
cache.TryGetValue("z", out _); // miss
cache.Compact(1.0);            // evicts both entries

// GetCurrentStatistics() returns null when TrackStatistics is false.
var stats = cache.GetCurrentStatistics()!;
var hitRatio = (double)stats.TotalHits / (stats.TotalHits + stats.TotalMisses);

Console.WriteLine($"hits={stats.TotalHits} misses={stats.TotalMisses} hitRatio={hitRatio:P0}");
Console.WriteLine($"evictions={stats.TotalEvictions} entries={stats.CurrentEntryCount}");
```

Output:

```text
hits=1 misses=1 hitRatio=50%
evictions=2 entries=0
```

## When you still need MeteredMemoryCache

- **You are on .NET 8 to 10.** The `MemoryCache` metrics first shipped in `Microsoft.Extensions.Caching.Memory` `11.0.0-preview.4.26230.115`. On earlier versions, `MemoryCache` has only `GetCurrentStatistics()`. The `MeteredMemoryCache` project in this repo targets `net10.0` and is consumed as source.
- **You cache through another `IMemoryCache` implementation.** The built-in instruments live inside `MemoryCache`. `MeteredMemoryCache` decorates any `IMemoryCache`. Its `cache.estimated_size` instrument still requires the inner cache to be a `MemoryCache` with `TrackStatistics` on.
- **You need extra tags on the cache metrics.** `MeteredMemoryCacheOptions.AdditionalTags` adds tags to every instrument. `MemoryCacheOptions` has no tag option; its only metrics-related settings are `TrackStatistics` and `Name`.
- **You want keyed, named registrations.** `AddNamedMeteredMemoryCache("name")` registers a keyed `IMemoryCache` per name. `AddMemoryCache()` registers one `IMemoryCache`.

A short .NET 10 equivalent:

```csharp
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNamedMeteredMemoryCache("products", options =>
    options.AdditionalTags["region"] = "us-west-2");

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(MeteredMemoryCache.MeterName)
        .AddConsoleExporter());

using var host = builder.Build();
await host.StartAsync();

var cache = host.Services.GetRequiredKeyedService<IMemoryCache>("products");
cache.Set("sku-1", "Widget");
cache.TryGetValue("sku-1", out _);

host.Services.GetRequiredService<MeterProvider>().ForceFlush();
await host.StopAsync();
```

This emits `cache.requests`, `cache.evictions`, and `cache.entries` with `cache.name=products` and `region=us-west-2`. See the [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md) for the full API.

## Migrating from MeteredMemoryCache to built-in metrics

| MeteredMemoryCache (.NET 8 to 10)                                                                                                      | Built-in (.NET 11)                                                                                                                | Action                                                                                                |
| -------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Meter `Microsoft.Extensions.Caching.Memory.MemoryCache`                                                                                | Same meter name                                                                                                                   | Keep `AddMeter`. One `AddMeter` call subscribes to both, so do not run both on one cache (see below). |
| `cache.requests` (`{requests}`)                                                                                                        | `dotnet.cache.requests` (`{request}`)                                                                                             | Rename in dashboards and alerts.                                                                      |
| `cache.evictions` (`{evictions}`)                                                                                                      | `dotnet.cache.evictions` (`{eviction}`)                                                                                           | Rename. Neither counts `Remove` or replacement.                                                       |
| `cache.entries` (`{entries}`)                                                                                                          | `dotnet.cache.entries` (`{entry}`)                                                                                                | Rename.                                                                                               |
| `cache.estimated_size` (`By`)                                                                                                          | `dotnet.cache.estimated_size` (`By`)                                                                                              | Rename.                                                                                               |
| Tag `cache.request.type` (`hit`, `miss`)                                                                                               | Tag `dotnet.cache.request.type` (`hit`, `miss`)                                                                                   | Rename in queries.                                                                                    |
| Tag `cache.name`                                                                                                                       | Tag `dotnet.cache.name`                                                                                                           | Rename in queries and group-bys.                                                                      |
| `cacheName` argument or `MeteredMemoryCacheOptions.CacheName`, default `"Default"`                                                     | `MemoryCacheOptions.Name`, default `"Default"`                                                                                    | Move the name to `AddMemoryCache(o => o.Name = "...")`.                                               |
| On whenever the decorator wraps a cache                                                                                                | Requires `TrackStatistics = true`; otherwise no instruments                                                                       | Set `TrackStatistics = true`.                                                                         |
| `cache.estimated_size` registered only when the inner cache is a `MemoryCache` with `TrackStatistics`; reports `0` without `SizeLimit` | Instrument created with `TrackStatistics`; reports a value only when `SizeLimit` is set (every entry then needs `Size`)           | Set `SizeLimit` and entry sizes if you chart size. Without them, expect no series instead of a `0`.   |
| `MeteredMemoryCacheOptions.AdditionalTags`                                                                                             | Not available                                                                                                                     | Keep MeteredMemoryCache if you need per-cache custom tags.                                            |
| `AddNamedMeteredMemoryCache` or `DecorateMemoryCacheWithMetrics`                                                                       | `AddMemoryCache(o => { o.TrackStatistics = true; o.Name = "..."; })`                                                              | Replace the registration. For several caches, construct each `MemoryCache` with its own `Name`.       |
| `MeteredMemoryCache.GetCurrentStatistics()` returns `CacheStatistics` (includes `TotalEvictions`)                                      | `MemoryCache.GetCurrentStatistics()` returns `MemoryCacheStatistics?` with new `TotalEvictions`; `null` without `TrackStatistics` | Switch the type and handle `null`.                                                                    |

Steps:

1. Upgrade to .NET 11 and `Microsoft.Extensions.Caching.Memory` 11.0.
2. Replace `AddNamedMeteredMemoryCache` or `DecorateMemoryCacheWithMetrics` with `AddMemoryCache(o => { o.TrackStatistics = true; o.Name = "..."; })`.
3. Keep `AddMeter("Microsoft.Extensions.Caching.Memory.MemoryCache")`.
4. Rename metrics and tags in dashboards, recording rules, and alerts (see the table below).
5. Confirm with `dotnet-counters` or the `MeterListener` repro that only `dotnet.cache.*` names appear.

### Do not run both on one cache

Tested on .NET 11 RC with this repo's `MeteredMemoryCache` wrapping a `MemoryCache` that has `TrackStatistics = true`:

- Both sets of four instruments were published, on two separate `Meter` objects that share the name `Microsoft.Extensions.Caching.Memory.MemoryCache`. One `AddMeter` call collects both.
- Every hit and miss was reported twice: once as `cache.requests` with `cache.name`, and once as `dotnet.cache.requests` with `dotnet.cache.name`.
- The two sets can disagree. `cache.entries` counts only entries written through the wrapper. Without a `SizeLimit`, `cache.estimated_size` reported `0` while `dotnet.cache.estimated_size` reported nothing.
- `DecorateMemoryCacheWithMetrics` over `AddMemoryCache(o => o.TrackStatistics = true)` produced both sets. `AddNamedMeteredMemoryCache` produced only `cache.*`, because it creates its inner `MemoryCache` with `TrackStatistics` off.

Pick one per cache. On .NET 11, either use the built-in metrics and remove the decorator, or keep `MeteredMemoryCache` and leave `TrackStatistics` off on the inner cache.

### Dashboard and PromQL renames

Exporters change the names. The OpenTelemetry Prometheus exporter (`OpenTelemetry.Exporter.Prometheus.HttpListener` `1.19.1-beta.1`) replaced dots with underscores and added `_total` and unit suffixes:

| MeteredMemoryCache series    | Built-in series                     |
| ---------------------------- | ----------------------------------- |
| `cache_requests_total`       | `dotnet_cache_requests_total`       |
| `cache_evictions_total`      | `dotnet_cache_evictions_total`      |
| `cache_entries`              | `dotnet_cache_entries`              |
| `cache_estimated_size_bytes` | `dotnet_cache_estimated_size_bytes` |
| label `cache_name`           | label `dotnet_cache_name`           |
| label `cache_request_type`   | label `dotnet_cache_request_type`   |

Other backends may apply different rules. Check one scrape or export before you edit queries.

## FAQ

**Does IMemoryCache have built-in metrics?**
Yes, shipping in .NET 11 (available in previews/RC), `MemoryCache` emits `dotnet.cache.*` metrics when `TrackStatistics` is true. Earlier versions have only `GetCurrentStatistics()`.

**What meter name do I add to OpenTelemetry?**
`Microsoft.Extensions.Caching.Memory.MemoryCache`.

**Why do I see no cache metrics?**
`TrackStatistics` is off, the meter is not added, or you are below .NET 11. A disposed cache also reports nothing.

**Why is there no dotnet.cache.estimated_size series?**
`SizeLimit` is not set. The gauge reports a value only when the cache has a `SizeLimit`.

**How do I get cache hit ratio in .NET?**
Divide `hit` by `hit` plus `miss` from `dotnet.cache.requests`.

**What is the cache name tag called?**
`dotnet.cache.name`, set from `MemoryCacheOptions.Name` (default `"Default"`).

**Do I need MeteredMemoryCache on .NET 11?**
Usually not for the four metrics. Yes on .NET 8 to 10, or for custom tags or non-`MemoryCache` implementations.

**Can I run both?**
Not recommended on the same cache. Both use the same meter name, and you get duplicate data under two name sets.

## See also

- [README: Emitted Metrics](../README.md#emitted-metrics) and [Choosing an Approach](../README.md#choosing-an-approach)
- [MeteredMemoryCache Usage Guide](MeteredMemoryCache.md)
- [OpenTelemetry Integration Guide](OpenTelemetryIntegration.md)
- [Migration Guide](MigrationGuide.md)
- [FAQ](FAQ.md)
- [dotnet/runtime#126146: Add built-in OpenTelemetry metrics to MemoryCache](https://github.com/dotnet/runtime/pull/126146)
- [dotnet/runtime#124140: API proposal, Metrics for M.E.Caching.MemoryCache](https://github.com/dotnet/runtime/issues/124140)
- [Collecting metrics (Microsoft Learn)](https://learn.microsoft.com/dotnet/core/diagnostics/metrics-collection)
- [Creating metrics and IMeterFactory with DI (Microsoft Learn)](https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation)
- [MemoryCacheOptions.TrackStatistics (Microsoft Learn)](https://learn.microsoft.com/dotnet/api/microsoft.extensions.caching.memory.memorycacheoptions.trackstatistics)
- [dotnet-counters (Microsoft Learn)](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters)
- [OpenTelemetry .NET metrics getting started](https://opentelemetry.io/docs/languages/dotnet/metrics/getting-started-console/)
