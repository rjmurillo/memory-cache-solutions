using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace CacheImplementations;

/// <summary>
/// Validates MeteredMemoryCacheOptions using the standard .NET options validation pattern.
/// </summary>
public sealed class MeteredMemoryCacheOptionsValidator : IValidateOptions<MeteredMemoryCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, MeteredMemoryCacheOptions options)
    {
        var failures = new List<string>();

        // Validate CacheName
        if (options.CacheName != null && string.IsNullOrWhiteSpace(options.CacheName))
        {
            failures.Add("CacheName, if specified, must be non-empty.");
        }

        // Validate AdditionalTags keys
        if (options.AdditionalTags.Any(kvp => string.IsNullOrWhiteSpace(kvp.Key)))
        {
            failures.Add("AdditionalTags keys must be non-empty.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
