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
        services.AddNamedMeteredMemoryCache("my-cache", options => options.SizeLimit = 123, meterName: "custom-meter");

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();
        var meter = provider.GetRequiredService<Meter>();

        Assert.NotNull(cache);
        Assert.NotNull(meter);
        Assert.Equal("custom-meter", meter.Name);

        // Should be a MeteredMemoryCache
        Assert.IsType<MeteredMemoryCache>(cache);
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
