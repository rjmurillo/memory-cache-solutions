namespace CacheImplementations;

/// <summary>
/// Internal helper for building pre-allocated tag arrays for Observable instrument callbacks.
/// Shared by <see cref="MeteredMemoryCache"/> and <see cref="OptimizedMeteredMemoryCache"/> to avoid duplication.
/// </summary>
internal static class TagBuilder
{
    /// <summary>
    /// Builds a pre-allocated tag array for Observable instrument callbacks.
    /// </summary>
    /// <param name="cacheName">The normalized cache name (must not be null or empty).</param>
    /// <param name="additionalTags">Optional additional tags to include. The "cache.name" key is automatically deduplicated.</param>
    /// <returns>A pre-allocated array of tags with "cache.name" as the first entry.</returns>
    internal static KeyValuePair<string, object?>[] BuildTags(
        string cacheName,
        IDictionary<string, object?>? additionalTags)
    {
        // cacheName is always non-empty (NormalizeCacheName returns "Default" for null/empty/whitespace)
        var tagList = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("cache.name", cacheName),
        };

        if (additionalTags != null)
        {
#pragma warning disable S3267 // Intentionally avoiding LINQ Where() allocation for performance
            foreach (var kvp in additionalTags)
            {
                if (!string.Equals(kvp.Key, "cache.name", StringComparison.Ordinal))
                {
                    var normalizedKey = kvp.Key?.Trim();
                    if (!string.IsNullOrEmpty(normalizedKey))
                    {
                        tagList.Add(new KeyValuePair<string, object?>(normalizedKey, kvp.Value));
                    }
                }
            }
#pragma warning restore S3267
        }

        return tagList.ToArray();
    }
}
