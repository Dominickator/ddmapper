using AutoMapper;
using AutoMapper.Features;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 3 features system: registration, global-once-at-build,
/// mapping before/after hooks, and runtime features reachable from resolvers.</summary>
public class FeaturesTests
{
    public class Source { public int Id { get; set; } public string? Name { get; set; } }
    public class Dest { public int Id { get; set; } public string? Name { get; set; } public string? Tag { get; set; } }

    public class TagFeature : IRuntimeFeature
    {
        public string Tag { get; set; } = "tagged";
    }

    public class TagResolver : IValueResolver<Source, Dest, string?>
    {
        public string? Resolve(Source source, Dest destination, string? destMember, ResolutionContext context) =>
            context.Features.Get<TagFeature>()?.Tag
            ?? context.RuntimeFeatures.Get<TagFeature>()?.Tag;
    }

    public class CountingGlobalFeature : IGlobalFeature
    {
        public static int Calls;
        public void Configure(IMapperConfigurationExpression configuration) { Calls++; }
    }

    public class AddMapGlobalFeature : IGlobalFeature
    {
        public void Configure(IMapperConfigurationExpression configuration)
        {
            configuration.CreateMap<Source, Dest>();
        }
    }

    public class LoggingMappingFeature : IMappingFeature
    {
        public static List<string> Log { get; } = new();
        public static string? AfterName;
        public void BeforeMap(object source, object destination, ResolutionContext context) => Log.Add("feature-before");
        public void AfterMap(object source, object destination, ResolutionContext context)
        {
            Log.Add("feature-after");
            AfterName = ((Dest)destination).Name;
        }
    }

    public class DualFeature : IMappingFeature, IRuntimeFeature
    {
        public static int BeforeCalls;
        public void BeforeMap(object source, object destination, ResolutionContext context) => BeforeCalls++;
        public void AfterMap(object source, object destination, ResolutionContext context) { }
    }

    [Fact]
    public void GlobalFeatureRunsOnceAtBuildNotPerMap()
    {
        CountingGlobalFeature.Calls = 0;
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>();
            cfg.AddFeature(new CountingGlobalFeature());
        });
        Assert.Equal(1, CountingGlobalFeature.Calls);
        var mapper = config.CreateMapper();
        mapper.Map<Dest>(new Source { Id = 1, Name = "Ada" });
        mapper.Map<Dest>(new Source { Id = 2, Name = "Bo" });
        config.CreateMapper();
        Assert.Equal(1, CountingGlobalFeature.Calls);
    }

    [Fact]
    public void GlobalFeatureCanRegisterAMap()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddFeature(new AddMapGlobalFeature());
        }).CreateMapper();
        var dest = mapper.Map<Dest>(new Source { Id = 5, Name = "Grace" });
        Assert.Equal(5, dest.Id);
        Assert.Equal("Grace", dest.Name);
    }

    [Fact]
    public void MappingFeatureBeforeAndAfterFireAroundMemberAssignment()
    {
        LoggingMappingFeature.Log.Clear();
        LoggingMappingFeature.AfterName = null;
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .BeforeMap((s, d) => LoggingMappingFeature.Log.Add("type-before"))
                .AfterMap((s, d) => LoggingMappingFeature.Log.Add("type-after"));
            cfg.AddFeature(new LoggingMappingFeature());
        }).CreateMapper();
        var dest = mapper.Map<Dest>(new Source { Id = 1, Name = "Ada" });
        Assert.Equal("Ada", dest.Name);
        // Features run outermost: before the map's own hooks, after them.
        Assert.Equal(new[] { "feature-before", "type-before", "type-after", "feature-after" },
            LoggingMappingFeature.Log);
        // AfterMap runs after members are assigned, so it sees the mapped value.
        Assert.Equal("Ada", LoggingMappingFeature.AfterName);
    }

    [Fact]
    public void RuntimeFeatureReachableFromResolver()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Tag, o => o.MapFrom<TagResolver>(new TagResolver()));
            cfg.AddFeature(new TagFeature { Tag = "from-feature" });
        }).CreateMapper();
        Assert.Equal("from-feature", mapper.Map<Dest>(new Source { Id = 1 }).Tag);
    }

    [Fact]
    public void RuntimeFeatureAbsentYieldsNullFromResolver()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Tag, o => o.MapFrom<TagResolver>(new TagResolver()));
        }).CreateMapper();
        Assert.Null(mapper.Map<Dest>(new Source { Id = 1 }).Tag);
    }

    [Fact]
    public void FeaturesRegistryAddGetTryGetAndCount()
    {
        var features = new Features<IRuntimeFeature>();
        Assert.Equal(0, features.Count);
        Assert.Null(features.Get<TagFeature>());
        Assert.False(features.TryGet<TagFeature>(out var missing));
        Assert.Null(missing);
        features.Add(new TagFeature { Tag = "one" });
        features.Add(new TagFeature { Tag = "two" });
        Assert.Equal(2, features.Count);
        Assert.Equal("one", features.Get<TagFeature>()!.Tag);
        Assert.True(features.TryGet<TagFeature>(out var found));
        Assert.Equal("one", found!.Tag);
        Assert.Equal(2, features.All.Count);
    }

    [Fact]
    public void GenericAddFeatureRegistersInEveryImplementedRegistry()
    {
        DualFeature.BeforeCalls = 0;
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>();
            // Explicit type argument: the plain overloads are ambiguous for a
            // type implementing two feature interfaces, which is exactly what
            // the generic AddFeature<T> dispatch is for.
            cfg.AddFeature<DualFeature>(new DualFeature());
        });
        Assert.Equal(1, config.MappingFeatures.Count);
        Assert.Equal(1, config.RuntimeFeatures.Count);
        config.CreateMapper().Map<Dest>(new Source { Id = 1 });
        Assert.Equal(1, DualFeature.BeforeCalls);
    }

    [Fact]
    public void AddFeatureRejectsNonFeatureType()
    {
        Assert.Throws<AutoMapperConfigurationException>(() =>
            new MapperConfiguration(cfg => cfg.AddFeature(new object())));
    }
}
