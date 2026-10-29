using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Hooks and per-operation options: config-level BeforeMap/AfterMap,
/// per-operation actions (object and typed), Items flowing to resolvers,
/// mapping onto an existing destination, and null sources.</summary>
public class HooksAndOptionsTests
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
        public string? Note { get; set; }
        public string? Extra { get; set; }
    }

    [Fact]
    public void ConfigBeforeAndAfterMapRunInOrder()
    {
        var order = new List<string>();
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .BeforeMap((s, d) => order.Add("before"))
                .AfterMap((s, d) => order.Add("after"))
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        mapper.Map<Dest>(new Source());
        Assert.Equal(new[] { "before", "after" }, order);
    }

    [Fact]
    public void AfterMapCanSetComputedMembers()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .AfterMap((s, d) => d.Note = s.Name + "#" + s.Id)
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        var dest = mapper.Map<Dest>(new Source { Id = 3, Name = "Ada" });
        Assert.Equal("Ada#3", dest.Note);
    }

    [Fact]
    public void BeforeMapWithContextReceivesContext()
    {
        ResolutionContext? seen = null;
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .BeforeMap((s, d, ctx) => seen = ctx)
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        mapper.Map<Dest>(new Source());
        Assert.NotNull(seen);
    }

    [Fact]
    public void PerOperationObjectActionsRun()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        var ran = new List<string>();
        var dest = mapper.Map<Dest>(new Source { Name = "Ada" }, opts =>
        {
            opts.BeforeMapActions.Add((s, d) => ran.Add("before"));
            opts.AfterMapActions.Add((s, d) => ran.Add("after"));
        });
        Assert.Equal(new[] { "before", "after" }, ran);
        Assert.Equal("Ada", dest.Name);
    }

    [Fact]
    public void PerOperationTypedActionsRun()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        string? beforeName = null;
        string? afterName = null;
        var dest = mapper.Map<Source, Dest>(new Source { Name = "Bo" }, opts =>
        {
            opts.TypedBeforeMapActions.Add((s, d) => beforeName = s.Name);
            opts.TypedAfterMapActions.Add((s, d) => afterName = d.Name);
        });
        Assert.Equal("Bo", beforeName);
        Assert.Equal("Bo", afterName);
        Assert.Equal("Bo", dest.Name);
    }

    public class SuffixResolver : IValueResolver<Source, Dest, string?>
    {
        public string? Resolve(Source source, Dest destination, string? destMember, ResolutionContext context)
        {
            var suffix = context.Items.TryGetValue("Suffix", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
            return source.Name + suffix;
        }
    }

    [Fact]
    public void ItemsFlowToResolvers()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Name, o => o.MapFrom(new SuffixResolver()))
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        var dest = mapper.Map<Source, Dest>(new Source { Name = "Cy" }, opts => opts.Items["Suffix"] = "!");
        Assert.Equal("Cy!", dest.Name);
    }

    [Fact]
    public void MapOntoExistingDestinationReturnsSameInstance()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore())
        ).CreateMapper();
        var existing = new Dest();
        var dest = mapper.Map(new Source { Id = 8, Name = "Dee" }, existing);
        Assert.Same(existing, dest);
        Assert.Equal(8, dest.Id);
        Assert.Equal("Dee", dest.Name);
    }

    [Fact]
    public void UnmappedMembersAreLeftAloneOnExistingDestination()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>()).CreateMapper();
        var existing = new Dest { Note = "keep", Extra = "keep too" };
        var dest = mapper.Map(new Source { Id = 1, Name = "Eli" }, existing);
        Assert.Equal("keep", dest.Note);
        Assert.Equal("keep too", dest.Extra);
        Assert.Equal("Eli", dest.Name);
    }

    [Fact]
    public void NullSourceMapsToNullForReferenceDestination()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>()).CreateMapper();
        Assert.Null(mapper.Map<Dest>((Source)null!));
    }

    [Fact]
    public void NullSourceMemberSkippedWhenFlagIsOff()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AllowNullDestinationValues = false;
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Note, o => o.Ignore())
                .ForMember(d => d.Extra, o => o.Ignore());
        }).CreateMapper();
        var existing = new Dest { Name = "Keep" };
        var dest = mapper.Map(new Source { Name = null }, existing);
        Assert.Equal("Keep", dest.Name);
    }
}
