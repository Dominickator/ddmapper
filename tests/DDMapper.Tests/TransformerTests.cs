using AutoMapper;
using AutoMapper.QueryableExtensions;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 4: value transformers on the object-mapping path.
/// Order is global (registration order) -&gt; map-level -&gt; member-level;
/// only values a map actually produces are transformed, so a kept
/// destination value (UseDestinationValue, failed condition) is untouched.
/// Projection rejects transformers rather than dropping them.</summary>
public class TransformerTests
{
    public class Src
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public Inner? Inner { get; set; }
    }

    public class Dst
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public InnerDto? Inner { get; set; }
    }

    public class Inner
    {
        public string? Text { get; set; }
    }

    public class InnerDto
    {
        public string? Text { get; set; }
    }

    public class UpperConverter : ITypeConverter<object, string?>
    {
        public string? Convert(object source, string? destination, ResolutionContext context) =>
            source?.ToString()?.ToUpperInvariant();
    }

    [Fact]
    public void MultipleMemberTransformersRunInRegistrationOrder()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m =>
                {
                    m.AddValueTransformer(v => v + "1");
                    m.AddValueTransformer(v => v + "2");
                })).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = "x" });
        Assert.Equal("x12", dest.Name);
    }

    [Fact]
    public void GlobalThenMapThenMemberTransformerOrder()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddValueTransformer<string>(v => v + "g1");
            cfg.AddValueTransformer<string>(v => v + "g2");
            cfg.CreateMap<Src, Dst>()
                .AddValueTransformer<string>(v => v + "p")
                .ForMember(d => d.Name, m => m.AddValueTransformer(v => v + "m"));
        }).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = "x" });
        Assert.Equal("xg1g2pm", dest.Name);
    }

    [Fact]
    public void GlobalTransformerAppliesOnlyToCoveredMemberTypes()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddValueTransformer<string>(v => v + "!");
            cfg.CreateMap<Src, Dst>();
        }).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = "a", Code = "b" });
        Assert.Equal("a!", dest.Name);
        Assert.Equal("b!", dest.Code);
    }

    [Fact]
    public void TransformerRunsAfterMemberConverter()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m =>
                {
                    m.ConvertUsing(new UpperConverter());
                    m.AddValueTransformer(v => v + "!");
                })).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = "hello" });
        Assert.Equal("HELLO!", dest.Name);
    }

    [Fact]
    public void TransformerAppliesToNullSubstitute()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m =>
                {
                    m.NullSubstitute("sub");
                    m.AddValueTransformer(v => v + "!");
                })).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = null });
        Assert.Equal("sub!", dest.Name);
    }

    [Fact]
    public void TransformerDoesNotTouchKeptDestinationValue()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m =>
                {
                    m.UseDestinationValue();
                    m.AddValueTransformer(v => v + "!");
                })).CreateMapper();
        var existing = new Dst { Name = "kept" };
        var dest = mapper.Map(new Src { Name = "new" }, existing);
        Assert.Equal("kept", dest.Name);
    }

    [Fact]
    public void TransformerOnNestedMemberIsHonored()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Src, Dst>();
            cfg.CreateMap<Inner, InnerDto>()
                .ForMember(d => d.Text, m => m.AddValueTransformer(v => v + "?"));
        }).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Inner = new Inner { Text = "deep" } });
        Assert.NotNull(dest.Inner);
        Assert.Equal("deep?", dest.Inner!.Text);
    }

    [Fact]
    public void GlobalTransformerAppliesToNestedMembersToo()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddValueTransformer<string>(v => v + "*");
            cfg.CreateMap<Src, Dst>();
            cfg.CreateMap<Inner, InnerDto>();
        }).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = "n", Inner = new Inner { Text = "t" } });
        Assert.Equal("n*", dest.Name);
        Assert.Equal("t*", dest.Inner!.Text);
    }

    [Fact]
    public void MapLevelTransformerAppliesToEveryMemberOfThatMap()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>().AddValueTransformer<string>(v => v + "#")).CreateMapper();
        var dest = mapper.Map<Dst>(new Src { Name = "a", Code = "b" });
        Assert.Equal("a#", dest.Name);
        Assert.Equal("b#", dest.Code);
    }

    // ---- Projection ----

    [Fact]
    public void ProjectionRejectsMemberTransformer()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m => m.AddValueTransformer(v => v + "!")));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsMapTransformer()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Src, Dst>().AddValueTransformer<string>(v => v + "!"));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsGlobalTransformerCoveringAMember()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddValueTransformer<string>(v => v + "!");
            cfg.CreateMap<Src, Dst>();
        });
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionIgnoresGlobalTransformerForUncoveredTypes()
    {
        // A global transformer for int covers no member of this map, so
        // projection still builds and runs.
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddValueTransformer<int>(v => v + 1);
            cfg.CreateMap<Src, Dst>();
            cfg.CreateMap<Inner, InnerDto>();
        });
        var results = new List<Src> { new() { Name = "ada" } }.AsQueryable()
            .ProjectTo<Src, Dst>(config).ToList();
        Assert.Equal("ada", results[0].Name);
    }
}
