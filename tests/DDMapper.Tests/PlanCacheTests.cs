using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Plan caching: plans are compiled once per pair per configuration
/// and reused, and CompileMappings warms the cache without mapping.</summary>
public class PlanCacheTests
{
    public class Source
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class Dest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void SecondMapReusesCompiledPlan()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>());
        var mapper = config.CreateMapper();
        Assert.Empty(config.CompiledPlans);
        mapper.Map<Dest>(new Source { Id = 1, Name = "a" });
        Assert.Single(config.CompiledPlans);
        mapper.Map<Dest>(new Source { Id = 2, Name = "b" });
        Assert.Single(config.CompiledPlans);
    }

    [Fact]
    public void GetOrBuildReturnsSameDelegateInstance()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>());
        var map = config.FindTypeMap(typeof(Source), typeof(Dest))!;
        var first = PlanCache.GetOrBuild(config, map);
        var second = PlanCache.GetOrBuild(config, map);
        Assert.Same(first, second);
    }

    [Fact]
    public void CompileMappingsWarmsCacheWithoutMapping()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>());
        config.CompileMappings();
        Assert.Single(config.CompiledPlans);
        var dest = config.CreateMapper().Map<Dest>(new Source { Id = 9, Name = "z" });
        Assert.Equal(9, dest.Id);
        Assert.Single(config.CompiledPlans);
    }

    [Fact]
    public void CachesArePerConfiguration()
    {
        var configA = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>());
        var configB = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>().ForMember(d => d.Name, o => o.MapFrom(s => s.Name!.ToUpperInvariant()))
        );
        var a = configA.CreateMapper().Map<Dest>(new Source { Name = "ada" });
        var b = configB.CreateMapper().Map<Dest>(new Source { Name = "ada" });
        Assert.Equal("ada", a.Name);
        Assert.Equal("ADA", b.Name);
        Assert.Single(configA.CompiledPlans);
        Assert.Single(configB.CompiledPlans);
        Assert.NotSame(configA.CompiledPlans[(typeof(Source), typeof(Dest))],
            configB.CompiledPlans[(typeof(Source), typeof(Dest))]);
    }
}
