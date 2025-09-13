using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace CacheImplementations;

/// <summary>
/// Validates <see cref="MeteredMemoryCacheOptions"/> instances using the standard .NET options validation pattern.
/// </summary>
/// <remarks>
/// <para>
/// This validator implements <see cref="IValidateOptions{TOptions}"/> to provide runtime validation
/// of <see cref="MeteredMemoryCacheOptions"/> configuration. It ensures that cache names and additional
/// tags meet the requirements for OpenTelemetry metric emission and dimensional tagging.
/// </para>
/// <para>
/// The validator is automatically registered when using <see cref="ServiceCollectionExtensions"/>
/// dependency injection methods and integrates with the .NET options validation pipeline.
/// Validation failures result in detailed error messages that identify specific configuration issues.
/// </para>
/// </remarks>
/// <seealso cref="MeteredMemoryCacheOptions"/>
/// <seealso cref="ServiceCollectionExtensions"/>
/// <seealso cref="IValidateOptions{TOptions}"/>
public sealed class MeteredMemoryCacheOptionsValidator : IValidateOptions<MeteredMemoryCacheOptions>
{
    /// <summary>
    /// Validates the specified <see cref="MeteredMemoryCacheOptions"/> instance against business rules and constraints.
    /// </summary>
    /// <param name="name">The name of the options instance being validated. Can be <see langword="null"/> for unnamed options.</param>
    /// <param name="options">The <see cref="MeteredMemoryCacheOptions"/> instance to validate. Cannot be <see langword="null"/>.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> indicating validation success or failure with detailed error messages.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs the following validations:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="MeteredMemoryCacheOptions.CacheName"/>: If specified (not <see langword="null"/>), 
    /// must be non-empty and contain non-whitespace characters to ensure valid metric tagging.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="MeteredMemoryCacheOptions.AdditionalTags"/>: All dictionary keys must be non-empty 
    /// and contain non-whitespace characters to comply with OpenTelemetry tag naming requirements.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Validation failures return a <see cref="ValidateOptionsResult"/> with specific error messages
    /// identifying the problematic configuration values. Multiple validation errors are collected
    /// and returned together for comprehensive feedback.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
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

        // Validate AdditionalTags values
        if (options.AdditionalTags.Any(kvp => kvp.Value is null))
        {
            failures.Add("AdditionalTags values cannot be null.");
        }

        // Validate AdditionalTags value types (should be string, number, or boolean)
        var invalidValueTypes = options.AdditionalTags
            .Where(kvp => kvp.Value is not null && 
                         kvp.Value is not string && 
                         kvp.Value is not int && 
                         kvp.Value is not long && 
                         kvp.Value is not double && 
                         kvp.Value is not float && 
                         kvp.Value is not decimal && 
                         kvp.Value is not bool)
            .ToList();

        if (invalidValueTypes.Any())
        {
            var invalidTypes = invalidValueTypes.Select(kvp => $"{kvp.Key}:{kvp.Value?.GetType().Name}")
                .Take(5) // Limit to first 5 for readability
                .ToList();
            failures.Add($"AdditionalTags values must be string, number, or boolean. Invalid types: {string.Join(", ", invalidTypes)}");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
