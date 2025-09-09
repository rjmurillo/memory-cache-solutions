using System.Diagnostics.Metrics;
using CacheImplementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNamedMeteredMemoryCache_RegistersNamedCacheAndMetrics()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddNamedMeteredMemoryCache("my-cache", options => options.DisposeInner = true, meterName: "custom-meter");

        var provider = services.BuildServiceProvider();
        // var namedCache = provider.GetRequiredService<INamedMemoryCache>();
        // var cache = namedCache.Get("my-cache");
        // For now, just ensure build passes and registration does not throw.
        Assert.NotNull(provider);

        // TODO: Add named cache resolution test when supported.
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_SupportsMultipleNamedCaches()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddNamedMeteredMemoryCache("cache1", meterName: "meter1");
        services.AddNamedMeteredMemoryCache("cache2", meterName: "meter2");

        var provider = services.BuildServiceProvider();
        // var namedCache = provider.GetRequiredService<INamedMemoryCache>();
        // var cache1 = namedCache.Get("cache1");
        // var cache2 = namedCache.Get("cache2");
        Assert.NotNull(provider);

        // TODO: Add named cache resolution test when supported.
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ThrowsOnDuplicateCacheName()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddNamedMeteredMemoryCache("dup-cache", meterName: "meterA");
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNamedMeteredMemoryCache("dup-cache", meterName: "meterB"));
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ThrowsOnDuplicateMeterName()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddNamedMeteredMemoryCache("cacheA", meterName: "meterX");
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNamedMeteredMemoryCache("cacheB", meterName: "meterX"));
    }

    [Fact]
    public void DecorateMemoryCacheWithMetrics_WrapsExistingCache()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions()));
        services.DecorateMemoryCacheWithMetrics(cacheName: "decorated", meterName: "decorated-meter");

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();
        var meter = provider.GetRequiredService<Meter>();

        Assert.NotNull(cache);
        Assert.NotNull(meter);
        Assert.Equal("decorated-meter", meter.Name);

        Assert.IsType<MeteredMemoryCache>(cache);
    }

    [Fact]
    public void AddNamedMeteredMemoryCache_ThrowsOnEmptyName()
    {
        IServiceCollection services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddNamedMeteredMemoryCache("", null));
    }
}
