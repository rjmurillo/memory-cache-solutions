namespace Unit;

internal static class SharedUtilities
{
    // Helper method to generate unique test names to prevent cross-test isolation issues
    internal static string GetUniqueTestName(string prefix = "test") => $"{prefix}-{Guid.NewGuid():N}";
    internal static string GetUniqueMeterName(string prefix = "meter") => $"{prefix}-{Guid.NewGuid():N}";
    internal static string GetUniqueCacheName(string prefix = "cache") => $"{prefix}-{Guid.NewGuid():N}";
}
