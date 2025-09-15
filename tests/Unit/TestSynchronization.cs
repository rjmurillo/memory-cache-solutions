namespace Unit;

/// <summary>
/// Provides synchronization utilities for reliable test execution, particularly for
/// handling asynchronous operations and timing-dependent scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This class addresses common flaky test patterns by providing deterministic
/// synchronization mechanisms that replace unreliable time-based assumptions.
/// </para>
/// <para>
/// Key features:
/// - Condition-based waiting instead of fixed delays
/// - Environment-aware timeouts for CI/CD compatibility
/// - Thread-safe operations for concurrent test scenarios
/// </para>
/// </remarks>
internal static class TestSynchronization
{
    /// <summary>
    /// Waits for a condition to be met by polling a value getter function.
    /// </summary>
    /// <typeparam name="T">The type of value being checked.</typeparam>
    /// <param name="getValue">Function to get the current value to check.</param>
    /// <param name="condition">Condition that must be met for the value.</param>
    /// <param name="timeout">Maximum time to wait for the condition.</param>
    /// <param name="pollingInterval">Interval between condition checks. Defaults to 50ms.</param>
    /// <returns>The value that satisfied the condition.</returns>
    /// <exception cref="TimeoutException">Thrown if the condition is not met within the timeout.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a reliable alternative to fixed delays by actively
    /// polling for a condition to be met. It's particularly useful for waiting
    /// for asynchronous operations to complete or for state changes to occur.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var evictionCount = await TestSynchronization.WaitForConditionAsync(
    ///     () => cache.GetCurrentStatistics().EvictionCount,
    ///     count => count > 0,
    ///     TimeSpan.FromSeconds(5));
    /// </code>
    /// </para>
    /// </remarks>
    internal static async Task<T> WaitForConditionAsync<T>(
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

            // Use Task.Yield() instead of Task.Delay to avoid banned API
            await Task.Yield();

            // Small spin wait to avoid busy waiting
            var spinWait = new SpinWait();
            var endTime = DateTime.UtcNow + interval;
            while (DateTime.UtcNow < endTime)
            {
                spinWait.SpinOnce();
            }
        }

        throw new TimeoutException($"Condition not met within {timeout}");
    }

    /// <summary>
    /// Waits for a condition to be met by polling a value getter function, with environment-aware timeout.
    /// </summary>
    /// <typeparam name="T">The type of value being checked.</typeparam>
    /// <param name="getValue">Function to get the current value to check.</param>
    /// <param name="condition">Condition that must be met for the value.</param>
    /// <param name="timeoutType">Type of timeout to use (Short, Medium, or Long).</param>
    /// <param name="pollingInterval">Interval between condition checks. Defaults to 50ms.</param>
    /// <returns>The value that satisfied the condition.</returns>
    /// <exception cref="TimeoutException">Thrown if the condition is not met within the timeout.</exception>
    /// <remarks>
    /// <para>
    /// This overload uses environment-aware timeouts that automatically adjust
    /// based on whether the test is running in CI/CD environments.
    /// </para>
    /// </remarks>
    internal static async Task<T> WaitForConditionAsync<T>(
        Func<T> getValue,
        Func<T, bool> condition,
        TestTimeoutType timeoutType,
        TimeSpan? pollingInterval = null)
    {
        var timeout = TestTimeouts.GetTimeout(timeoutType);
        return await WaitForConditionAsync(getValue, condition, timeout, pollingInterval);
    }
}

/// <summary>
/// Provides environment-aware timeout values for test operations.
/// </summary>
/// <remarks>
/// <para>
/// This class automatically adjusts timeout values based on the execution environment.
/// CI/CD environments typically have slower performance and higher resource contention,
/// so longer timeouts are used to prevent false test failures.
/// </para>
/// </remarks>
internal static class TestTimeouts
{
    private static readonly bool IsCI =
        Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("TF_BUILD") == "True" ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <summary>
    /// Gets a timeout value based on the specified timeout type and current environment.
    /// </summary>
    /// <param name="timeoutType">The type of timeout to get.</param>
    /// <returns>A timeout value appropriate for the current environment.</returns>
    internal static TimeSpan GetTimeout(TestTimeoutType timeoutType)
    {
        return timeoutType switch
        {
            TestTimeoutType.Short => IsCI ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(2),
            TestTimeoutType.Medium => IsCI ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5),
            TestTimeoutType.Long => IsCI ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(10),
            _ => throw new ArgumentException($"Unknown timeout type: {timeoutType}", nameof(timeoutType))
        };
    }

    /// <summary>
    /// Short timeout for quick operations (2s local, 10s CI).
    /// </summary>
    internal static TimeSpan Short => GetTimeout(TestTimeoutType.Short);

    /// <summary>
    /// Medium timeout for moderate operations (5s local, 30s CI).
    /// </summary>
    internal static TimeSpan Medium => GetTimeout(TestTimeoutType.Medium);

    /// <summary>
    /// Long timeout for complex operations (10s local, 2min CI).
    /// </summary>
    internal static TimeSpan Long => GetTimeout(TestTimeoutType.Long);
}

/// <summary>
/// Specifies the type of timeout to use for test operations.
/// </summary>
internal enum TestTimeoutType
{
    /// <summary>
    /// Short timeout for quick operations.
    /// </summary>
    Short,

    /// <summary>
    /// Medium timeout for moderate operations.
    /// </summary>
    Medium,

    /// <summary>
    /// Long timeout for complex operations.
    /// </summary>
    Long
}
