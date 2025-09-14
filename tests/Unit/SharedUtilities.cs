namespace Unit;

/// <summary>
/// Provides utility methods for generating unique names in test scenarios to ensure test isolation
/// and prevent cross-test contamination that can cause flaky test results.
/// </summary>
/// <remarks>
/// <para>
/// This class is critical for maintaining test reliability by ensuring that each test run
/// uses unique identifiers for meters, cache names, and other test resources. This prevents
/// tests from interfering with each other and eliminates flaky test failures caused by
/// resource name collisions.
/// </para>
/// <para>
/// All methods use <see cref="Guid.NewGuid()"/> with the "N" format to generate compact,
/// URL-safe unique identifiers that are appended to the provided prefix.
/// </para>
/// <para>
/// <strong>MANDATORY USAGE:</strong> All tests in this codebase MUST use these methods
/// instead of hard-coded strings to ensure proper test isolation.
/// </para>
/// </remarks>
internal static class SharedUtilities
{
    /// <summary>
    /// Generates a unique test name by appending a GUID to the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to use for the test name. Defaults to "test".</param>
    /// <returns>A unique test name in the format "{prefix}-{GUID}".</returns>
    /// <remarks>
    /// <para>
    /// This method is essential for preventing test name collisions that can cause
    /// cross-test contamination and flaky test results. Always use this method instead
    /// of hard-coded test names.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var testName = SharedUtilities.GetUniqueTestName("my-test");
    /// // Result: "my-test-a1b2c3d4e5f6789012345678901234ab"
    /// </code>
    /// </para>
    /// </remarks>
    internal static string GetUniqueTestName(string prefix = "test") => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Generates a unique meter name by appending a GUID to the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to use for the meter name. Defaults to "meter".</param>
    /// <returns>A unique meter name in the format "{prefix}-{GUID}".</returns>
    /// <remarks>
    /// <para>
    /// This method is critical for OpenTelemetry meter isolation in tests. Each test
    /// must use a unique meter name to prevent metrics from different tests interfering
    /// with each other.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// using var meter = new Meter(SharedUtilities.GetUniqueMeterName("test.metered.cache"));
    /// </code>
    /// </para>
    /// <para>
    /// <strong>CRITICAL:</strong> Never use hard-coded meter names in tests as this
    /// will cause cross-test contamination and flaky test results.
    /// </para>
    /// </remarks>
    internal static string GetUniqueMeterName(string prefix = "meter") => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Generates a unique cache name by appending a GUID to the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to use for the cache name. Defaults to "cache".</param>
    /// <returns>A unique cache name in the format "{prefix}-{GUID}".</returns>
    /// <remarks>
    /// <para>
    /// This method ensures that each test uses a unique cache name, preventing
    /// cache state from persisting between tests and causing unexpected behavior.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var cacheName = SharedUtilities.GetUniqueCacheName("test-cache");
    /// var cache = new MeteredMemoryCache(innerCache, meter, cacheName);
    /// </code>
    /// </para>
    /// <para>
    /// <strong>IMPORTANT:</strong> Always use this method for cache names in tests
    /// to ensure proper test isolation and prevent cache state contamination.
    /// </para>
    /// </remarks>
    internal static string GetUniqueCacheName(string prefix = "cache") => $"{prefix}-{Guid.NewGuid():N}";
}
