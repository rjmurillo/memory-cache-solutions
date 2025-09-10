using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CacheImplementations;

/// <summary>
/// Configuration options for <see cref="MeteredMemoryCache"/> behavior and OpenTelemetry metrics emission.
/// Provides control over cache naming, disposal behavior, and dimensional tagging for enhanced observability.
/// </summary>
/// <remarks>
/// This class follows the .NET options pattern and integrates with <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>
/// for dependency injection scenarios. All properties support runtime validation through data annotations.
/// </remarks>
public class MeteredMemoryCacheOptions
{
    /// <summary>
    /// Gets or sets the logical name for the cache instance, used as the "cache.name" dimensional tag in all emitted metrics.
    /// </summary>
    /// <value>
    /// A string representing the cache name, or <see langword="null"/> for unnamed caches.
    /// When <see langword="null"/>, no "cache.name" tag will be added to metrics.
    /// </value>
    /// <remarks>
    /// The cache name enables distinguishing metrics from multiple <see cref="MeteredMemoryCache"/> instances
    /// in monitoring dashboards and alerting systems. Use descriptive names like "user-session", "product-catalog",
    /// or "feature-flags" to clearly identify the cache's purpose.
    /// </remarks>
    public string? CacheName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="MeteredMemoryCache"/> should dispose the inner 
    /// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> when this decorator is disposed.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to dispose the inner cache during disposal; otherwise, <see langword="false"/>.
    /// The default value is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Set to <see langword="true"/> when <see cref="MeteredMemoryCache"/> owns the inner cache instance
    /// (e.g., created specifically for the decorated cache). Set to <see langword="false"/> when the inner
    /// cache is shared or managed by the dependency injection container to prevent premature disposal.
    /// </remarks>
    public bool DisposeInner { get; set; } = false;

    private IDictionary<string, object?> _additionalTags = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets additional dimensional tags to include in all emitted OpenTelemetry metrics.
    /// </summary>
    /// <value>
    /// A dictionary containing tag names and values. Cannot be <see langword="null"/>.
    /// The default value is an empty <see cref="Dictionary{TKey, TValue}"/> with ordinal string comparison.
    /// </value>
    /// <remarks>
    /// <para>
    /// Additional tags enable rich dimensional metrics for filtering, grouping, and aggregation in monitoring systems.
    /// Common use cases include environment labels ("environment" = "production"), service identification 
    /// ("service" = "user-api"), or regional tagging ("region" = "us-west-2").
    /// </para>
    /// <para>
    /// The "cache.name" tag is reserved and managed by the <see cref="CacheName"/> property.
    /// Any "cache.name" entries in this dictionary will be ignored to maintain consistency.
    /// </para>
    /// <para>
    /// Tag values should be of types compatible with OpenTelemetry semantic conventions, typically
    /// <see langword="string"/>, numeric types, or <see langword="bool"/>. Complex objects may not
    /// serialize correctly in all exporters.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when attempting to set the value to <see langword="null"/>.
    /// </exception>
    [Required]
    public IDictionary<string, object?> AdditionalTags
    {
        get => _additionalTags;
        set => _additionalTags = value ?? throw new ArgumentNullException(nameof(value));
    }
}
