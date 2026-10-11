---
paths:
  - "tests/**"
---

## Test Isolation and Unique Name Generation (CRITICAL)

**MANDATORY: All AI agents and contributors MUST use `SharedUtilities` for generating unique names in tests.**

### Prohibited Patterns (Will Cause Test Flakiness)

**NEVER use hard-coded strings for test names:**

```csharp
// ❌ PROHIBITED - Causes cross-test contamination and flakiness
using var meter = new Meter("test");
using var meter = new Meter("test.metered.cache");
using var meter = new Meter("test.accuracy.1");
```

### Required Patterns (Test Isolation)

**ALWAYS use `SharedUtilities` for unique name generation:**

```csharp
// ✅ REQUIRED - Ensures test isolation and prevents flakiness
using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test"));
using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.metered.cache"));
using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.accuracy.1"));

// For cache names
var cacheName = SharedUtilities.GetUniqueCacheName("test-cache");

// For general test names
var testName = SharedUtilities.GetUniqueTestName("test-prefix");
```

### Available Methods

The `SharedUtilities` class provides these methods in the `Unit` namespace:

- `GetUniqueMeterName(string prefix = "meter")` - Generates unique meter names
- `GetUniqueCacheName(string prefix = "cache")` - Generates unique cache names
- `GetUniqueTestName(string prefix = "test")` - Generates unique test names

### Test Listener and Harness Updates

When using `TestListener` or `MetricCollectionHarness`, pass the actual meter name:

```csharp
// ✅ CORRECT - Use the actual meter name for filtering
using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.metered.cache"));
using var listener = new TestListener(meter.Name, "cache.requests");
using var harness = new MetricCollectionHarness(meter.Name, "cache.requests");
```

### Enforcement Rules

- **Any test using hard-coded meter names will be rejected during review**
- **Cross-test contamination due to name collisions is a critical bug**
- **All new tests MUST use `SharedUtilities` for name generation**
- **Existing tests with hard-coded names MUST be updated before merge**

### Rationale

Hard-coded names cause:

- Cross-test contamination (tests interfering with each other)
- Flaky test results (non-deterministic failures)
- CI instability (tests pass/fail randomly)
- Difficult debugging (metrics from multiple tests mixed together)

### Validation Checklist

Before committing any test changes:

- [ ] All `new Meter("...")` calls use `SharedUtilities.GetUniqueMeterName()`
- [ ] All `TestListener` constructors use `meter.Name` parameter
- [ ] All `MetricCollectionHarness` constructors use `meter.Name` parameter
- [ ] No hard-coded strings for cache names, test names, or meter names
- [ ] Tests run in isolation without cross-contamination

**Violation of these rules will result in immediate rejection and rework.**

### Enforcement Strategy

**Enforcement: Code Review Checklist**

Until the analyzer is implemented, use this manual checklist:

- [ ] All `new Meter("...")` calls use `SharedUtilities.GetUniqueMeterName()`
- [ ] No string interpolation in Meter constructors without SharedUtilities
- [ ] No string concatenation in Meter constructors without SharedUtilities
- [ ] All test files scanned for hard-coded meter names
- [ ] Cross-test contamination risks assessed

---

## Test Reliability and Avoiding Flaky Tests (CRITICAL)

**MANDATORY: All AI agents and contributors MUST follow these guidelines to prevent flaky tests.**

### Core Principles for Reliable Tests

1. **Tests must be deterministic** - Same input should always produce same output
2. **Tests must be isolated** - No shared state between tests
3. **Tests must be timing-independent** - No assumptions about execution speed
4. **Tests must handle concurrency correctly** - Proper synchronization for multi-threaded scenarios

### Common Flaky Test Patterns to AVOID

#### 1. Time-Based Assumptions (NEVER DO THIS)

```csharp
// ❌ WRONG - Assumes operation completes within fixed time
entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1);
await Task.Delay(5); // Hope it's evicted by now
Assert.True(evicted); // Flaky!

// ✅ CORRECT - Use synchronization primitives
var evictionSignal = new TaskCompletionSource<bool>();
entry.RegisterPostEvictionCallback((key, value, reason, state) =>
{
    ((TaskCompletionSource<bool>)state).TrySetResult(true);
}, evictionSignal);

await evictionSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
```

#### 2. Uncontrolled Background Operations

```csharp
// ❌ WRONG - Background refresh timing is unpredictable
var value1 = await cache.GetOrCreateSwrAsync(...);
await Task.Yield(); // Hope refresh happens
var value2 = await cache.GetOrCreateSwrAsync(...);
Assert.NotEqual(value1, value2); // Flaky!

// ✅ CORRECT - Track background operations explicitly
var refreshStarted = new TaskCompletionSource<bool>();
var refreshCompleted = new TaskCompletionSource<bool>();

async Task<T> Factory(CancellationToken ct)
{
    refreshStarted.TrySetResult(true);
    var result = await DoWork();
    refreshCompleted.TrySetResult(true);
    return result;
}

await refreshStarted.Task.WaitAsync(timeout);
await refreshCompleted.Task.WaitAsync(timeout);
```

#### 3. Race Conditions in Concurrent Tests

```csharp
// ❌ WRONG - Multiple threads updating shared state
Parallel.For(0, 10, i =>
{
    services.AddNamedCache($"cache-{i}");
});
var provider = services.BuildServiceProvider(); // Race condition!

// ✅ CORRECT - Synchronize concurrent operations
var services = new ServiceCollection();
var lockObj = new object();

Parallel.For(0, 10, i =>
{
    lock (lockObj)
    {
        services.AddNamedCache($"cache-{i}");
    }
});
```

#### 4. Metric Collection Timing Issues

```csharp
// ❌ WRONG - Metrics might not be ready immediately
cache.Set("key1", "value1");
cache.Set("key2", "value2"); // Should evict key1
var metrics = GetMetrics();
Assert.NotNull(metrics); // Flaky!

// ✅ CORRECT - Wait for metrics with retry logic
cache.Set("key1", "value1");
cache.Set("key2", "value2");

var metrics = await WaitForMetricsAsync(
    expectedCondition: m => m.EvictionCount > 0,
    timeout: TimeSpan.FromSeconds(10),
    pollingInterval: TimeSpan.FromMilliseconds(100));
```

### Essential Test Patterns for Reliability

#### 1. Synchronization Helpers

```csharp
// Create reusable helpers for common synchronization needs
public static class TestSynchronization
{
    public static async Task<T> WaitForConditionAsync<T>(
        Func<T> getValue,
        Func<T, bool> condition,
        TimeSpan timeout,
        TimeSpan? pollingInterval = null)
    {
        var interval = pollingInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var value = getValue();
            if (condition(value))
                return value;

            await Task.Delay(interval);
        }

        throw new TimeoutException($"Condition not met within {timeout}");
    }
}
```

#### 2. Deterministic Eviction Testing

```csharp
// Use cancellation tokens for deterministic evictions
var cts = new CancellationTokenSource();
var evictionCount = 0;

cache.Set("key", "value", new MemoryCacheEntryOptions
{
    ExpirationTokens = { new CancellationChangeToken(cts.Token) },
    PostEvictionCallbacks =
    {
        new PostEvictionCallbackRegistration
        {
            EvictionCallback = (key, value, reason, state) =>
            {
                Interlocked.Increment(ref evictionCount);
            }
        }
    }
});

// Trigger eviction deterministically
cts.Cancel();

// Wait for callback with timeout
await TestSynchronization.WaitForConditionAsync(
    () => Volatile.Read(ref evictionCount),
    count => count > 0,
    TimeSpan.FromSeconds(5));
```

#### 3. Thread-Safe Test State

```csharp
// Always use thread-safe collections and atomic operations
private readonly ConcurrentBag<int> _results = new();
private int _operationCount;

// In concurrent test
Parallel.For(0, 100, async i =>
{
    var result = await DoOperation();
    _results.Add(result);
    Interlocked.Increment(ref _operationCount);
});

// Wait for all operations
await TestSynchronization.WaitForConditionAsync(
    () => Volatile.Read(ref _operationCount),
    count => count == 100,
    TimeSpan.FromSeconds(10));
```

### Environment-Specific Considerations

#### CI/CD Environment Adjustments

```csharp
// Use environment-aware timeouts
public static class TestTimeouts
{
    private static readonly bool IsCI =
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("TF_BUILD") == "True" ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    public static TimeSpan Short => IsCI ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(2);
    public static TimeSpan Medium => IsCI ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
    public static TimeSpan Long => IsCI ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(10);
}

// Usage
await operation.WaitAsync(TestTimeouts.Medium);
```

### Test Implementation Checklist

Before committing any test:

- [ ] **No fixed delays** - Replace all `Task.Delay` with proper synchronization
- [ ] **No time-based assumptions** - Use signals, not timeouts for verification
- [ ] **Proper cleanup** - Dispose all resources, cancel all operations
- [ ] **Thread safety** - All shared state uses proper synchronization
- [ ] **Deterministic triggers** - Use cancellation tokens, not time-based expiration
- [ ] **Retry logic** - Add retry mechanisms for operations that might need time to stabilize
- [ ] **CI-aware timeouts** - Use longer timeouts for CI environments
- [ ] **Volatile/Interlocked** - Use for all cross-thread state access
- [ ] **Unique names** - Use `SharedUtilities` for all test names
- [ ] **Metric waiting** - Never assume metrics are immediately available

### Debugging Flaky Tests

When a test is flaky:

1. **Add diagnostic logging** with timestamps
2. **Run test in a loop** (1000+ times) to reproduce
3. **Use stress testing** with reduced CPU/memory
4. **Check for shared state** between test runs
5. **Verify cleanup** is complete before next test
6. **Add progress indicators** to identify where test gets stuck

### Example: Fully Reliable Eviction Test

```csharp
[Fact]
public async Task Cache_Eviction_EmitsMetrics_Reliably()
{
    // Unique names prevent cross-test contamination
    var meterName = SharedUtilities.GetUniqueMeterName("eviction-test");
    var cacheName = SharedUtilities.GetUniqueCacheName("test-cache");

    using var meter = new Meter(meterName);
    using var cache = new MeteredMemoryCache(
        new MemoryCache(new MemoryCacheOptions { SizeLimit = 1 }),
        meter,
        cacheName);

    // Track evictions with thread-safe counter
    var evictionCount = 0;
    var evictionSignal = new TaskCompletionSource<bool>();

    // Set up metric listener before operations
    using var listener = new TestListener(meterName, "cache.evictions");

    // First entry
    cache.Set("key1", "value1", new MemoryCacheEntryOptions
    {
        Size = 1,
        PostEvictionCallbacks =
        {
            new PostEvictionCallbackRegistration
            {
                EvictionCallback = (key, value, reason, state) =>
                {
                    Interlocked.Increment(ref evictionCount);
                    evictionSignal.TrySetResult(true);
                }
            }
        }
    });

    // Second entry should trigger eviction
    cache.Set("key2", "value2", new MemoryCacheEntryOptions { Size = 1 });

    // Wait for eviction callback with timeout
    await evictionSignal.Task.WaitAsync(TestTimeouts.Short);

    // Wait for metrics to be available
    var metrics = await TestSynchronization.WaitForConditionAsync(
        () => listener.GetMeasurements("cache.evictions"),
        m => m.Count > 0,
        TestTimeouts.Medium);

    // Verify
    Assert.Equal(1, Volatile.Read(ref evictionCount));
    Assert.Single(metrics);
    Assert.Equal(1, metrics.First().Value);
}
```

### Common Root Causes of Flaky Tests

1. **MemoryCache evictions are async** - Always wait for callbacks
2. **Metrics export is buffered** - Add retry logic for metric collection
3. **Background threads need time** - Use synchronization, not delays
4. **ServiceCollection isn't thread-safe** - Synchronize concurrent registrations
5. **Cache operations aren't atomic** - Use locks for complex updates
6. **CI environments are slower** - Increase timeouts appropriately
7. **Reflection might not work consistently** - Prefer test interfaces/hooks

**Violation of these guidelines will result in test failures and required rework.**

---
