using System.ComponentModel.DataAnnotations;

using CacheImplementations;

namespace Unit;

public class MeteredMemoryCacheOptionsTests
{
    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        // Arrange & Act
        var options = new MeteredMemoryCacheOptions();

        // Assert
        Assert.Null(options.CacheName);
        Assert.False(options.DisposeInner);
        Assert.NotNull(options.AdditionalTags);
        Assert.Empty(options.AdditionalTags);
        Assert.IsType<Dictionary<string, object?>>(options.AdditionalTags);
    }

    [Fact]
    public void CacheName_CanBeSetAndRetrieved()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();
        const string expectedName = "test-cache";

        // Act
        options.CacheName = expectedName;

        // Assert
        Assert.Equal(expectedName, options.CacheName);
    }

    [Fact]
    public void DisposeInner_CanBeSetAndRetrieved()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act
        options.DisposeInner = true;

        // Assert
        Assert.True(options.DisposeInner);
    }

    [Fact]
    public void AdditionalTags_CanBeModified()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act
        options.AdditionalTags["key1"] = "value1";
        options.AdditionalTags["key2"] = 42;
        options.AdditionalTags["key3"] = null;

        // Assert
        Assert.Equal(3, options.AdditionalTags.Count);
        Assert.Equal("value1", options.AdditionalTags["key1"]);
        Assert.Equal(42, options.AdditionalTags["key2"]);
        Assert.Null(options.AdditionalTags["key3"]);
    }

    [Fact]
    public void AdditionalTags_UsesOrdinalStringComparer()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act
        options.AdditionalTags["Key"] = "value1";
        options.AdditionalTags["KEY"] = "value2";

        // Assert
        Assert.Equal(2, options.AdditionalTags.Count);
        Assert.Equal("value1", options.AdditionalTags["Key"]);
        Assert.Equal("value2", options.AdditionalTags["KEY"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("valid-cache-name")]
    [InlineData("Cache_With_Underscores")]
    [InlineData("Cache123")]
    public void CacheName_AcceptsValidValues(string? cacheName)
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act & Assert (should not throw)
        options.CacheName = cacheName;
        Assert.Equal(cacheName, options.CacheName);
    }

    [Fact]
    public void AdditionalTags_RequiredAttribute_IsPresent()
    {
        // Arrange
        var property = typeof(MeteredMemoryCacheOptions).GetProperty(nameof(MeteredMemoryCacheOptions.AdditionalTags));

        // Act
        var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), false);

        // Assert
        Assert.NotNull(property);
        Assert.NotNull(requiredAttribute);
        Assert.Single(requiredAttribute);
    }

    [Fact]
    public void AdditionalTags_CannotBeSetToNull()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act & Assert
        Assert.Throws<System.ArgumentNullException>(() => options.AdditionalTags = null!);
    }

    [Theory]
    [MemberData(nameof(GetValidAdditionalTagsData))]
    public void AdditionalTags_AcceptsValidTagCollections(IDictionary<string, object?> tags)
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions();

        // Act
        options.AdditionalTags = tags;

        // Assert
        Assert.Equal(tags, options.AdditionalTags);
    }

    public static IEnumerable<object[]> GetValidAdditionalTagsData()
    {
        yield return new object[] { new Dictionary<string, object?>(StringComparer.Ordinal) };
        yield return new object[] { new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = "value" } };
        yield return new object[] { new Dictionary<string, object?>(StringComparer.Ordinal) { ["key1"] = 1, ["key2"] = "string", ["key3"] = null } };
    }

    [Fact]
    public void Properties_ArePublicAndSettable()
    {
        // Arrange
        var optionsType = typeof(MeteredMemoryCacheOptions);

        // Act & Assert
        var cacheNameProperty = optionsType.GetProperty(nameof(MeteredMemoryCacheOptions.CacheName));
        Assert.NotNull(cacheNameProperty);
        Assert.True(cacheNameProperty.CanRead);
        Assert.True(cacheNameProperty.CanWrite);
        Assert.True(cacheNameProperty.SetMethod?.IsPublic);

        var disposeInnerProperty = optionsType.GetProperty(nameof(MeteredMemoryCacheOptions.DisposeInner));
        Assert.NotNull(disposeInnerProperty);
        Assert.True(disposeInnerProperty.CanRead);
        Assert.True(disposeInnerProperty.CanWrite);
        Assert.True(disposeInnerProperty.SetMethod?.IsPublic);

        var additionalTagsProperty = optionsType.GetProperty(nameof(MeteredMemoryCacheOptions.AdditionalTags));
        Assert.NotNull(additionalTagsProperty);
        Assert.True(additionalTagsProperty.CanRead);
        Assert.True(additionalTagsProperty.CanWrite);
        Assert.True(additionalTagsProperty.SetMethod?.IsPublic);
    }
}

public class MeteredMemoryCacheOptionsValidatorTests
{
    private readonly MeteredMemoryCacheOptionsValidator _validator = new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            DisposeInner = true,
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key1"] = "value1",
                ["key2"] = 42
            }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.FailureMessage);
        Assert.Empty(result.Failures ?? Enumerable.Empty<string>());
    }

    [Fact]
    public void Validate_NullCacheName_ReturnsSuccess()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = null,
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Validate_EmptyOrWhitespaceCacheName_ReturnsFailure(string invalidCacheName)
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = invalidCacheName,
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("CacheName, if specified, must be non-empty.", result.Failures ?? Enumerable.Empty<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Validate_EmptyOrWhitespaceTagKeys_ReturnsFailure(string invalidKey)
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [invalidKey] = "value",
                ["validKey"] = "validValue"
            }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("AdditionalTags keys must be non-empty.", result.Failures ?? Enumerable.Empty<string>());
    }

    [Fact]
    public void Validate_MultipleInvalidTagKeys_ReturnsFailureWithSingleMessage()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [""] = "value1",
                ["   "] = "value2",
                ["validKey"] = "validValue"
            }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("AdditionalTags keys must be non-empty.", result.Failures ?? Enumerable.Empty<string>());
        // Should only contain the message once, not multiple times
        Assert.Single(result.Failures ?? Enumerable.Empty<string>(), f => f.Contains("AdditionalTags keys must be non-empty."));
    }

    [Fact]
    public void Validate_BothCacheNameAndTagKeysInvalid_ReturnsMultipleFailures()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "   ",
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [""] = "value"
            }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(2, (result.Failures ?? Enumerable.Empty<string>()).Count());
        Assert.Contains("CacheName, if specified, must be non-empty.", result.Failures ?? Enumerable.Empty<string>());
        Assert.Contains("AdditionalTags keys must be non-empty.", result.Failures ?? Enumerable.Empty<string>());
    }

    [Fact]
    public void Validate_ValidTagKeysWithNullValues_ReturnsFailure()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key1"] = null,
                ["key2"] = "value",
                ["key3"] = 42
            }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("AdditionalTags values cannot be null", result.FailureMessage);
    }

    [Fact]
    public void Validate_EmptyAdditionalTags_ReturnsSuccess()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("cache-with-dashes")]
    [InlineData("cache_with_underscores")]
    [InlineData("Cache123")]
    [InlineData("UPPERCASE")]
    [InlineData("mixedCase")]
    public void Validate_ValidCacheNames_ReturnsSuccess(string validCacheName)
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = validCacheName,
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NameParameterIsIgnored()
    {
        // Arrange
        var options = new MeteredMemoryCacheOptions
        {
            CacheName = "valid-cache",
            AdditionalTags = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        // Act & Assert - should work with null, empty, or any name
        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.True(_validator.Validate("", options).Succeeded);
        Assert.True(_validator.Validate("any-name", options).Succeeded);
    }
}
