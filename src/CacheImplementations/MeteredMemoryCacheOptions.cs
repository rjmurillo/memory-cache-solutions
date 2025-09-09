using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CacheImplementations;

/// <summary>
/// Options for configuring MeteredMemoryCache behavior and metrics.
/// </summary>
public class MeteredMemoryCacheOptions
{
    /// <summary>
    /// Logical name for the cache instance (used as the cache.name tag).
    /// </summary>
    public string? CacheName { get; set; }

    /// <summary>
    /// If true, MeteredMemoryCache will dispose the inner IMemoryCache when disposed.
    /// </summary>
    public bool DisposeInner { get; set; } = false;

    /// <summary>
    /// Additional tags to include in all emitted metrics.
    /// </summary>
    [Required]
    public IDictionary<string, object?> AdditionalTags { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
