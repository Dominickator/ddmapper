using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Explicit member configuration: MapFrom (lambda, two-arg lambda, resolver),
/// ForPath, Ignore, Condition, PreCondition, NullSubstitute, UseDestinationValue,
/// and member-level ConvertUsing.</summary>
public class MemberConfigTests
{
    public class Source
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Nickname { get; set; }
        public int Age { get; set; }
        public bool Flag { get; set; }
        public string? City { get; set; }
    }

    public class Dest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Display { get; set; }
        public string? AgeText { get; set; }
        public string? Conditional { get; set; }
        public string? PreChecked { get; set; }
        public string? Substituted { get; set; }
        public string? Kept { get; set; }
        public string? Formatted { get; set; }
        public Address? Address { get; set; }
        public string? Skipped { get; set; }
    }

    public class Address
    {
        public string? City { get; set; }
        public string? Street { get; set; }
    }

    private static IMapper MapperWith(Action<IMappingExpression<Source, Dest>> extra) =>
        new MapperConfiguration(cfg =>
        {
            var map = cfg.CreateMap<Source, Dest>();
            extra(map);
        }).CreateMapper();

    [Fact]
    public void MapFromLambda()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.Display, o => o.MapFrom(s => s.Name + " (" + s.Nickname + ")"))
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { Name = "Ada", Nickname = "Ace" });
        Assert.Equal("Ada (Ace)", dest.Display);
    }

    [Fact]
    public void MapFromTwoArgLambdaReceivesDestination()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.Display, o => o.MapFrom((s, d) => s.Name + "!" ))
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { Name = "Bo" });
        Assert.Equal("Bo!", dest.Display);
    }

    [Fact]
    public void MapFromConvertsMemberType()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.AgeText, o => o.MapFrom(s => s.Age))
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { Age = 36 });
        Assert.Equal("36", dest.AgeText);
    }

    public class UpperResolver : IValueResolver<Source, Dest, string?>
    {
        public string? Resolve(Source source, Dest destination, string? destMember, ResolutionContext context) =>
            source.Name?.ToUpperInvariant() ?? string.Empty;
    }

    [Fact]
    public void MapFromResolverClass()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.Display, o => o.MapFrom(new UpperResolver()))
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { Name = "cy" });
        Assert.Equal("CY", dest.Display);
    }

    [Fact]
    public void ForPathCreatesIntermediateObjects()
    {
        var mapper = MapperWith(m => m
            .ForPath(d => d.Address!.City, o => o.MapFrom(s => s.City))
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { City = "Kansas City" });
        Assert.NotNull(dest.Address);
        Assert.Equal("Kansas City", dest.Address!.City);
    }

    [Fact]
    public void ForPathIgnoreLeavesPathUntouched()
    {
        var mapper = MapperWith(m => m
            .ForPath(d => d.Address!.City, o => o.Ignore())
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { City = "Nowhere" });
        Assert.Null(dest.Address);
    }

    [Fact]
    public void IgnoreLeavesDefaultOnFreshDestination()
    {
        var mapper = MapperWith(m => m.ForMember(d => d.Name, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { Name = "Ada" });
        Assert.Null(dest.Name);
    }

    [Fact]
    public void IgnorePreservesExistingDestinationValue()
    {
        var mapper = MapperWith(m => m.ForMember(d => d.Name, o => o.Ignore()));
        var existing = new Dest { Name = "Keep me" };
        var dest = mapper.Map(new Source { Name = "Ada" }, existing);
        Assert.Same(existing, dest);
        Assert.Equal("Keep me", dest.Name);
    }

    [Fact]
    public void ConditionOneArgGatesAssignment()
    {
        var gated = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Conditional, o => { o.MapFrom(s => s.Name); o.Condition(s => s.Flag); })
                .ForMember(d => d.Skipped, o => o.Ignore());
        }).CreateMapper();
        Assert.Null(gated.Map<Dest>(new Source { Name = "Ada", Flag = false }).Conditional);
        Assert.Equal("Ada", gated.Map<Dest>(new Source { Name = "Ada", Flag = true }).Conditional);
    }

    [Fact]
    public void ConditionTwoAndThreeArgForms()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Conditional, o => { o.MapFrom(s => s.Name); o.Condition((s, d) => s.Flag && d.Id == 0); })
                .ForMember(d => d.PreChecked, o => { o.MapFrom(s => s.Name); o.Condition((s, d, m) => m != null && m.Length > 2); })
                .ForMember(d => d.Skipped, o => o.Ignore());
        }).CreateMapper();
        var off = mapper.Map<Dest>(new Source { Id = 9, Name = "Ada" });
        Assert.Null(off.Conditional);
        var on = mapper.Map<Dest>(new Source { Id = 0, Name = "Ada", Flag = true });
        Assert.Equal("Ada", on.Conditional);
        Assert.Equal("Ada", on.PreChecked);
        var short_ = mapper.Map<Dest>(new Source { Name = "Al" });
        Assert.Null(short_.PreChecked);
    }

    public class CountingResolver : IValueResolver<Source, Dest, string?>
    {
        public int Calls;
        public string? Resolve(Source source, Dest destination, string? destMember, ResolutionContext context)
        {
            Calls++;
            return source.Name;
        }
    }

    [Fact]
    public void PreConditionSkipsResolutionEntirely()
    {
        var resolver = new CountingResolver();
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.PreChecked, o => { o.MapFrom(resolver); o.PreCondition(s => s.Flag); })
                .ForMember(d => d.Skipped, o => o.Ignore());
        }).CreateMapper();
        var dest = mapper.Map<Dest>(new Source { Name = "Ada", Flag = false });
        Assert.Null(dest.PreChecked);
        Assert.Equal(0, resolver.Calls);
        dest = mapper.Map<Dest>(new Source { Name = "Ada", Flag = true });
        Assert.Equal("Ada", dest.PreChecked);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public void NullSubstituteReplacesNull()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.Substituted, o => { o.MapFrom(s => s.Name); o.NullSubstitute("N/A"); })
            .ForMember(d => d.Skipped, o => o.Ignore()));
        Assert.Equal("N/A", mapper.Map<Dest>(new Source { Name = null }).Substituted);
        Assert.Equal("Ada", mapper.Map<Dest>(new Source { Name = "Ada" }).Substituted);
    }

    [Fact]
    public void UseDestinationValueKeepsExistingValue()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.Kept, o => { o.MapFrom(s => s.Name); o.UseDestinationValue(); })
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var existing = new Dest { Kept = "Original" };
        var dest = mapper.Map(new Source { Name = "Ada" }, existing);
        Assert.Equal("Original", dest.Kept);
    }

    public class BracketConverter : ITypeConverter<object, string?>
    {
        public string? Convert(object source, string? destination, ResolutionContext context) =>
            "[" + source + "]";
    }

    [Fact]
    public void MemberConvertUsingAppliesConverter()
    {
        var mapper = MapperWith(m => m
            .ForMember(d => d.Formatted, o => { o.MapFrom(s => s.Age); o.ConvertUsing(new BracketConverter()); })
            .ForMember(d => d.Skipped, o => o.Ignore()));
        var dest = mapper.Map<Dest>(new Source { Age = 7 });
        Assert.Equal("[7]", dest.Formatted);
    }
}
