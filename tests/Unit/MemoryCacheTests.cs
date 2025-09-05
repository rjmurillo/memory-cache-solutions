using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;

public class MemoryCacheTests
{
    [Fact]
    public void BasicMemoryCache_SetAndGetValue()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var key = "k1";
        cache.Set(key, 42, TimeSpan.FromMinutes(1));

        var found = cache.TryGetValue(key, out int value);

        Assert.True(found);
        Assert.Equal(42, value);
    }
}
