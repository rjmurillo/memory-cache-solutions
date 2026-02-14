# Design Decisions

## Overview

This document explains key architectural decisions made in this reference implementation of the MemoryCache metrics proposal (dotnet/runtime#124140).

## Decorator Pattern vs BCL Internal Implementation

The spec proposes adding metrics directly inside `MemoryCache`. This reference implementation uses a **decorator pattern** (`MeteredMemoryCache` wraps `IMemoryCache`). This means:

- Users explicitly opt-in to metrics by wrapping their cache
- The decorator can work with ANY `IMemoryCache` implementation, not just `MemoryCache`
- No BCL modifications are required — this works on existing .NET versions
- The `enableMetrics`/`TrackStatistics` gate is implicit: using the decorator IS the opt-in

## Independent Counter Tracking

The spec relies on `MemoryCache.GetCurrentStatistics()` to read hit/miss/eviction counts. This reference implementation maintains **independent atomic counters** (`Interlocked` operations) because:

- The decorator wraps `IMemoryCache`, not `MemoryCache` specifically — `GetCurrentStatistics()` is not available on the interface
- Independent tracking works with any cache implementation
- Atomic counters (`Interlocked.Increment`) provide the same thread-safety guarantees as the BCL's internal counters
- Observable instruments poll these counters on demand, avoiding any hot-path allocation

## Eviction Filtering

The spec states evictions exclude "explicit user removals." This implementation excludes both:

- `EvictionReason.Removed` — explicit `cache.Remove(key)` calls
- `EvictionReason.Replaced` — entry overwritten by `cache.Set(key, newValue)` when key already exists

`Replaced` is excluded because overwriting a cache entry is an explicit user action (calling `Set`), not an automatic cache behavior. The BCL's `MemoryCache` fires eviction callbacks with `EvictionReason.Replaced` when an entry is overwritten, but this is semantically a "user update" rather than a cache-initiated eviction due to pressure, expiration, or size limits.

## Observable Instruments (Zero Hot-Path Overhead)

All instruments are Observable (polled by the metrics system) rather than recording on every cache operation. This matches the spec's explicit requirement: "All these instruments are observable to not create any bottleneck in hot path."

The implementation pre-allocates tag arrays during construction to ensure zero allocation during metric collection callbacks.

## cache.estimated_size Conditional Registration

The `cache.estimated_size` gauge is only registered when the inner cache is a `MemoryCache` with `TrackStatistics` enabled (i.e., `GetCurrentStatistics()` returns non-null). This is because:

- The `IMemoryCache` interface does not expose size statistics
- Only `MemoryCache` with `TrackStatistics = true` provides `CurrentEstimatedSize`
- Registering the gauge unconditionally would always report 0, which is misleading
