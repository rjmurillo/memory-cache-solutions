using CacheImplementations;

namespace Unit;

/// <summary>
/// Regression tests for enhanced MeteredMemoryCacheOptionsValidator.
/// </summary>
public class MeteredMemoryCacheOptionsValidatorEnhancedTests
{
    /// <summary>
    /// Tests that the validator rejects null tag values.
    /// </summary>
    [Fact]
    public void Validate_WithNullTagValues_ShouldFail()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "test-cache",
            AdditionalTags = { ["valid-key"] = "valid-value", ["null-key"] = null! }
        };

        var result = validator.Validate("test", options);

        Assert.False(result.Succeeded);
        Assert.Contains("AdditionalTags values cannot be null", result.FailureMessage);
    }

    /// <summary>
    /// Tests that the validator rejects invalid tag value types.
    /// </summary>
    [Fact]
    public void Validate_WithInvalidTagValueTypes_ShouldFail()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "test-cache",
            AdditionalTags = 
            { 
                ["valid-string"] = "valid",
                ["valid-number"] = 42,
                ["valid-bool"] = true,
                ["invalid-object"] = new object(),
                ["invalid-array"] = new[] { 1, 2, 3 }
            }
        };

        var result = validator.Validate("test", options);

        Assert.False(result.Succeeded);
        Assert.Contains("AdditionalTags values must be string, number, or boolean", result.FailureMessage);
        Assert.Contains("invalid-object:Object", result.FailureMessage);
        Assert.Contains("invalid-array:Int32[]", result.FailureMessage);
    }

    /// <summary>
    /// Tests that the validator accepts valid tag value types.
    /// </summary>
    [Fact]
    public void Validate_WithValidTagValueTypes_ShouldSucceed()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "test-cache",
            AdditionalTags = 
            { 
                ["string-tag"] = "valid-string",
                ["int-tag"] = 42,
                ["long-tag"] = 123L,
                ["double-tag"] = 3.14,
                ["float-tag"] = 2.71f,
                ["decimal-tag"] = 1.23m,
                ["bool-tag"] = true
            }
        };

        var result = validator.Validate("test", options);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// Tests that the validator handles empty AdditionalTags correctly.
    /// </summary>
    [Fact]
    public void Validate_WithEmptyAdditionalTags_ShouldSucceed()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "test-cache",
            AdditionalTags = { }
        };

        var result = validator.Validate("test", options);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// Tests that the validator still validates other properties correctly.
    /// </summary>
    [Fact]
    public void Validate_WithEmptyCacheName_ShouldFail()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "", // Invalid: empty cache name
            AdditionalTags = { ["valid-tag"] = "valid-value" }
        };

        var result = validator.Validate("test", options);

        Assert.False(result.Succeeded);
        Assert.Contains("CacheName, if specified, must be non-empty", result.FailureMessage);
    }

    /// <summary>
    /// Tests that the validator handles multiple validation failures correctly.
    /// </summary>
    [Fact]
    public void Validate_WithMultipleValidationFailures_ShouldFailWithAllMessages()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "", // Invalid: empty cache name
            AdditionalTags = 
            { 
                [""] = "valid-value", // Invalid: empty key
                ["null-key"] = null!, // Invalid: null value
                ["invalid-type"] = new object() // Invalid: wrong type
            }
        };

        var result = validator.Validate("test", options);

        Assert.False(result.Succeeded);
        Assert.Contains("CacheName, if specified, must be non-empty", result.FailureMessage);
        Assert.Contains("AdditionalTags keys must be non-empty", result.FailureMessage);
        Assert.Contains("AdditionalTags values cannot be null", result.FailureMessage);
        Assert.Contains("AdditionalTags values must be string, number, or boolean", result.FailureMessage);
    }

    /// <summary>
    /// Tests that the validator limits the number of invalid types shown in error messages.
    /// </summary>
    [Fact]
    public void Validate_WithManyInvalidTypes_ShouldLimitErrorMessageLength()
    {
        var validator = new MeteredMemoryCacheOptionsValidator();
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "test-cache",
            AdditionalTags = new Dictionary<string, object?>
            {
                ["invalid1"] = new object(),
                ["invalid2"] = new object(),
                ["invalid3"] = new object(),
                ["invalid4"] = new object(),
                ["invalid5"] = new object(),
                ["invalid6"] = new object(), // This should not appear in error message
                ["invalid7"] = new object()  // This should not appear in error message
            }
        };

        var result = validator.Validate("test", options);

        Assert.False(result.Succeeded);
        Assert.Contains("AdditionalTags values must be string, number, or boolean", result.FailureMessage);
        
        // Should only show first 5 invalid types
        Assert.Contains("invalid1:Object", result.FailureMessage);
        Assert.Contains("invalid5:Object", result.FailureMessage);
        Assert.DoesNotContain("invalid6:Object", result.FailureMessage);
        Assert.DoesNotContain("invalid7:Object", result.FailureMessage);
    }
}
