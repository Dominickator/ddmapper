using AutoMapper;
using AutoMapper.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>DI registration against a REAL Microsoft DI container (not the
/// fake container in DependencyInjectionTests). Requires the
/// Microsoft.Extensions.DependencyInjection package, restored from NuGet.</summary>
public class RealContainerTests
{
    public class Src
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class Dst
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class TestProfile : Profile
    {
        public TestProfile()
        {
            CreateMap<Src, Dst>();
        }
    }

    [Fact]
    public void RealContainerResolvesMapperAndMaps()
    {
        var services = new ServiceCollection();
        services.AddAutoMapper(cfg => cfg.CreateMap<Src, Dst>());
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var dest = mapper.Map<Dst>(new Src { Id = 7, Name = "real" });
        Assert.Equal(7, dest.Id);
        Assert.Equal("real", dest.Name);
        var config = provider.GetRequiredService<IConfigurationProvider>();
        Assert.Same(mapper.ConfigurationProvider, config);
    }

    [Fact]
    public void RealContainerRegistersMapperAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddDDMapper(cfg => cfg.CreateMap<Src, Dst>());
        var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<IMapper>(),
            provider.GetRequiredService<IMapper>());
    }

    [Fact]
    public void RealContainerProfileTypeOverload()
    {
        var services = new ServiceCollection();
        services.AddDDMapper(typeof(TestProfile));
        var provider = services.BuildServiceProvider();
        var dest = provider.GetRequiredService<IMapper>().Map<Dst>(new Src { Id = 3, Name = "prof" });
        Assert.Equal(3, dest.Id);
        Assert.Equal("prof", dest.Name);
    }
}
