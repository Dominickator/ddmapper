using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Constructor mapping: parameters matched by name, get-only properties
/// set through the constructor, ForCtorParam overrides, and ConstructUsing.</summary>
public class ConstructorTests
{
    public class Source
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Extra { get; set; }
    }

    public class ImmutableDest
    {
        public ImmutableDest(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }
        public string Name { get; }
    }

    [Fact]
    public void CtorParamsMatchedByName()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Source, ImmutableDest>()).CreateMapper();
        var dest = mapper.Map<ImmutableDest>(new Source { Id = 3, Name = "Ada" });
        Assert.Equal(3, dest.Id);
        Assert.Equal("Ada", dest.Name);
    }

    public class RenamedCtorDest
    {
        public RenamedCtorDest(int identifier, string label)
        {
            Identifier = identifier;
            Label = label;
        }

        public int Identifier { get; }
        public string Label { get; }
    }

    [Fact]
    public void ForCtorParamOverridesSourceMember()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, RenamedCtorDest>()
                .ForCtorParam("identifier", o => o.MapFrom(s => s.Id))
                .ForCtorParam("label", o => o.MapFrom(s => s.Name))
        ).CreateMapper();
        var dest = mapper.Map<RenamedCtorDest>(new Source { Id = 11, Name = "Bo" });
        Assert.Equal(11, dest.Identifier);
        Assert.Equal("Bo", dest.Label);
    }

    public class MixedDest
    {
        public MixedDest(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }
        public string Name { get; }
        public int Age { get; set; }
        public string? Extra { get; set; }
    }

    [Fact]
    public void CtorMembersAreNotAssignedTwice()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Source, MixedDest>()).CreateMapper();
        var dest = mapper.Map<MixedDest>(new Source { Id = 2, Name = "Cy", Age = 40, Extra = "x" });
        Assert.Equal(2, dest.Id);
        Assert.Equal("Cy", dest.Name);
        Assert.Equal(40, dest.Age);
        Assert.Equal("x", dest.Extra);
    }

    public class ConstructedDest
    {
        public ConstructedDest(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }
        public string Name { get; }
        public bool BuiltByFactory { get; set; }
    }

    [Fact]
    public void ConstructUsingReplacesConstruction()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, ConstructedDest>()
                .ConstructUsing(s => new ConstructedDest(s.Id * 10, s.Name!) { BuiltByFactory = true })
        ).CreateMapper();
        var dest = mapper.Map<ConstructedDest>(new Source { Id = 4, Name = "Dee" });
        Assert.Equal(40, dest.Id);
        Assert.Equal("Dee", dest.Name);
        Assert.True(dest.BuiltByFactory);
    }

    [Fact]
    public void ConstructUsingWithContextReceivesContext()
    {
        ResolutionContext? seen = null;
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, ConstructedDest>()
                .ConstructUsing((s, ctx) => { seen = ctx; return new ConstructedDest(s.Id, s.Name!); })
        ).CreateMapper();
        mapper.Map<ConstructedDest>(new Source { Id = 1, Name = "Eli" });
        Assert.NotNull(seen);
        Assert.NotNull(seen!.Mapper);
    }
}
