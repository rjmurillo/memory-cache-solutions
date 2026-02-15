using System.Collections.Frozen;

namespace CacheImplementations;

/// <summary>
/// Internal helper for building pre-allocated tag arrays for Observable instrument callbacks.
/// Used by <see cref="MeteredMemoryCache"/> to build instrument tags.
/// </summary>
internal static class TagBuilder
{
    /// <summary>
    /// Builds a pre-allocated tag array for Observable instrument callbacks.
    /// </summary>
    /// <param name="cacheName">The normalized cache name (must not be null or empty).</param>
    /// <param name="additionalTags">Optional additional tags to include. The "cache.name" key is automatically deduplicated.
    /// A defensive copy (frozen) is made to prevent post-construction mutations from affecting the cache.</param>
    /// <returns>A pre-allocated array of tags with "cache.name" as the first entry.</returns>
    internal static KeyValuePair<string, object?>[] BuildTags(
        string cacheName,
        IDictionary<string, object?>? additionalTags)
    {
        // Fast path: no additional tags — single-element array, no List allocation
        if (additionalTags is null or { Count: 0 })
        {
            return [new KeyValuePair<string, object?>("cache.name", cacheName)];
        }

        // Create a frozen snapshot to protect against concurrent modification of the source dictionary.
        // This ensures stable iteration even if the caller modifies AdditionalTags after calling this method.
        var frozenTags = additionalTags.ToFrozenDictionary(StringComparer.Ordinal);

        // Count eligible additional tags to allocate exact-sized array
        int extraCount = 0;
#pragma warning disable S3267 // Intentionally avoiding LINQ Where() allocation for performance
        foreach (var kvp in frozenTags)
        {
            var normalizedKey = kvp.Key?.Trim();
            if (!string.IsNullOrEmpty(normalizedKey) && !string.Equals(normalizedKey, "cache.name", StringComparison.Ordinal))
            {
                extraCount++;
            }
        }
#pragma warning restore S3267

        var tags = new KeyValuePair<string, object?>[1 + extraCount];
        tags[0] = new KeyValuePair<string, object?>("cache.name", cacheName);

        int index = 1;
#pragma warning disable S3267
        foreach (var kvp in frozenTags)
        {
            var normalizedKey = kvp.Key?.Trim();
            if (!string.IsNullOrEmpty(normalizedKey) && !string.Equals(normalizedKey, "cache.name", StringComparison.Ordinal))
            {
                tags[index++] = new KeyValuePair<string, object?>(normalizedKey, kvp.Value);
            }
        }
#pragma warning restore S3267

        return tags;
    }
}
