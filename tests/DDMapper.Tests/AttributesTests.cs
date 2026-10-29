using AutoMapper;
using AutoMapper.Configuration.Annotations;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 3 attribute-driven configuration: each attribute behavior,
/// fluent-over-attribute precedence, destination-over-source precedence, and
/// attributes carried on source members.</summary>
public class AttributesTests
{
    public class Customer { public string? Name { get; set; } }

    public class Source
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Nickname { get; set; }
        public string? Secret { get; set; }
        public string? Code { get; set; }
        public int Age { get; set; }
        public Customer? Customer { get; set; }
    }

    public class Dest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Display { get; set; }
        public string? CustomerName { get; set; }
        public string? Secret { get; set; }
        public string? Code { get; set; }
        public string? AgeText { get; set; }
        public string? Kept { get; set; }
        public string? Substituted { get; set; }
        public string? Computed { get; set; }
    }

    // Destination-side attribute fixtures (one destination type per behavior
    // so attributes on one member cannot leak into another test's map).
    public class IgnoreDest
    {
        public int Id { get; set; }
        [Ignore] public string? Name { get; set; }
    }
    public class RedirectDest
    {
        [SourceMember("Nickname")] public string? Display { get; set; }
    }
    public class DottedRedirectDest
    {
        [SourceMember("Customer.Name")] public string? CustomerName { get; set; }
    }
    public class SubstituteDest
    {
        [NullSubstitute("fallback")] public string? Name { get; set; }
    }
    public class KeepDest
    {
        public string? Kept { get; set; }
        [UseDestinationValue] public string? Name { get; set; }
    }
    public class ResolverDest
    {
        [ValueResolver(typeof(ShoutResolver))] public string? Name { get; set; }
    }
    public class ComputedDest
    {
        [ValueResolver(typeof(ComputedResolver))] public string? Computed { get; set; }
    }
    public class RuntimeDest
    {
        [MapAtRuntime] public string? Name { get; set; }
    }
    public class OrderDest
    {
        public static List<string> Log { get; } = new();
        private string? _a;
        [MappingOrder(2)] public string? A { get => _a; set { Log.Add("A"); _a = value; } }
        private string? _b;
        [MappingOrder(0)] public string? B { get => _b; set { Log.Add("B"); _b = value; } }
        private string? _c;
        [MappingOrder(1)] public string? C { get => _c; set { Log.Add("C"); _c = value; } }
    }
    public class OrderSource
    {
        public string? A { get; set; }
        public string? B { get; set; }
        public string? C { get; set; }
    }

    // Source-side attribute fixtures.
    public class SourceWithAttrs
    {
        [NullSubstitute("from-source")] public string? Name { get; set; }
        [Ignore] public string? Secret { get; set; }
        [ValueResolver(typeof(CodeResolver))] public string? Code { get; set; }
    }
    public class SourceWithAttrsDest
    {
        public string? Name { get; set; }
        public string? Secret { get; set; }
        public string? Code { get; set; }
    }
    public class DestBeatsSourceDest
    {
        [NullSubstitute("from-dest")] public string? Name { get; set; }
    }

    public class IntToTextConverter : ITypeConverter<object, string?>
    {
        public string? Convert(object source, string? destination, ResolutionContext context) =>
            source is int i ? $"age:{i}" : null;
    }
    public class ShoutResolver : IValueResolver<Source, ResolverDest, string?>
    {
        public string? Resolve(Source source, ResolverDest destination, string? destMember, ResolutionContext context) =>
            source.Name?.ToUpperInvariant();
    }
    public class ComputedResolver : IValueResolver<Source, ComputedDest, string?>
    {
        public string? Resolve(Source source, ComputedDest destination, string? destMember, ResolutionContext context) =>
            $"computed:{source.Id}";
    }
    public class CodeResolver : IValueResolver<SourceWithAttrs, SourceWithAttrsDest, string?>
    {
        public string? Resolve(SourceWithAttrs source, SourceWithAttrsDest destination, string? destMember, ResolutionContext context) =>
            $"code-{source.Code}";
    }

    public class BaseSrc { public string? Name { get; set; } }
    public class DerivedSrc : BaseSrc { public new string? Name { get; set; } }

    private static IMapper MapperFor<TSrc, TDest>(Action<IMapperConfigurationExpression>? extra = null) =>
        new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(t => Activator.CreateInstance(t)!);
            cfg.CreateMap<TSrc, TDest>();
            extra?.Invoke(cfg);
        }).CreateMapper();

    [Fact]
    public void IgnoreAttributeOnDestinationSkipsMember()
    {
        var dest = MapperFor<Source, IgnoreDest>().Map<IgnoreDest>(new Source { Id = 3, Name = "Ada" });
        Assert.Equal(3, dest.Id);
        Assert.Null(dest.Name);
    }

    [Fact]
    public void SourceMemberAttributeRedirects()
    {
        var dest = MapperFor<Source, RedirectDest>().Map<RedirectDest>(new Source { Nickname = "Ace" });
        Assert.Equal("Ace", dest.Display);
    }

    [Fact]
    public void SourceMemberAttributeDottedPathFlattens()
    {
        var dest = MapperFor<Source, DottedRedirectDest>().Map<DottedRedirectDest>(
            new Source { Customer = new Customer { Name = "Grace" } });
        Assert.Equal("Grace", dest.CustomerName);
    }

    [Fact]
    public void NullSubstituteAttributeSuppliesSubstitute()
    {
        var dest = MapperFor<Source, SubstituteDest>().Map<SubstituteDest>(new Source { Name = null });
        Assert.Equal("fallback", dest.Name);
        var kept = MapperFor<Source, SubstituteDest>().Map<SubstituteDest>(new Source { Name = "Ada" });
        Assert.Equal("Ada", kept.Name);
    }

    [Fact]
    public void UseDestinationValueAttributeKeepsExistingValue()
    {
        var mapper = MapperFor<Source, KeepDest>();
        var existing = new KeepDest { Name = "existing" };
        var dest = mapper.Map(new Source { Name = "new" }, existing);
        Assert.Equal("existing", dest.Name);
    }

    [Fact]
    public void ValueConverterAttributeOnMatchingMember()
    {
        // Dest member "Age" matches source Age by convention; the attribute
        // converter converts the int to a string destination member.
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(t => Activator.CreateInstance(t)!);
            cfg.CreateMap<Source, ConverterTextDest>();
        }).CreateMapper();
        Assert.Equal("age:30", mapper.Map<ConverterTextDest>(new Source { Age = 30 }).Age);
    }

    public class ConverterTextDest
    {
        [ValueConverter(typeof(IntToTextConverter))] public string? Age { get; set; }
    }

    [Fact]
    public void ValueResolverAttributeResolvesMember()
    {
        var dest = MapperFor<Source, ResolverDest>().Map<ResolverDest>(new Source { Name = "ada" });
        Assert.Equal("ADA", dest.Name);
    }

    [Fact]
    public void ValueResolverAttributeWorksWithoutMatchingSourceMember()
    {
        var dest = MapperFor<Source, ComputedDest>().Map<ComputedDest>(new Source { Id = 42 });
        Assert.Equal("computed:42", dest.Computed);
    }

    [Fact]
    public void MappingOrderAttributeControlsAssignmentOrder()
    {
        OrderDest.Log.Clear();
        MapperFor<OrderSource, OrderDest>().Map<OrderDest>(new OrderSource { A = "a", B = "b", C = "c" });
        Assert.Equal(new[] { "B", "C", "A" }, OrderDest.Log);
    }

    [Fact]
    public void MapAtRuntimeAttributeResolvesDerivedMember()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<BaseSrc, RuntimeDest>();
        }).CreateMapper();
        var runtime = new DerivedSrc { Name = "derived" };
        ((BaseSrc)runtime).Name = "base";
        // Map through the declared base type so the BaseSrc map is used while
        // the runtime source is the derived type carrying the hiding member.
        BaseSrc asBase = runtime;
        Assert.Equal("derived", mapper.Map<BaseSrc, RuntimeDest>(asBase).Name);
    }

    [Fact]
    public void FluentNullSubstituteOverridesAttribute()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, SubstituteDest>()
                .ForMember(d => d.Name, o => o.NullSubstitute("fluent"));
        }).CreateMapper();
        Assert.Equal("fluent", mapper.Map<SubstituteDest>(new Source { Name = null }).Name);
    }

    [Fact]
    public void FluentMapFromOverridesSourceMemberAttribute()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, RedirectDest>()
                .ForMember(d => d.Display, o => o.MapFrom(s => s.Name));
        }).CreateMapper();
        Assert.Equal("Ada", mapper.Map<RedirectDest>(new Source { Name = "Ada", Nickname = "Ace" }).Display);
    }

    [Fact]
    public void FluentIgnoreOverridesAttributeBehavior()
    {
        // Attribute substitute would supply "fallback" for a null Name; the
        // fluent Ignore must win, leaving the member untouched (null here
        // because the destination is fresh).
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, SubstituteDest>()
                .ForMember(d => d.Name, o => o.Ignore());
        }).CreateMapper();
        Assert.Null(mapper.Map<SubstituteDest>(new Source { Name = "Ada" }).Name);
    }

    [Fact]
    public void AttributeOnSourceMemberNullSubstitute()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(t => Activator.CreateInstance(t)!);
            cfg.CreateMap<SourceWithAttrs, SourceWithAttrsDest>();
        }).CreateMapper();
        Assert.Equal("from-source", mapper.Map<SourceWithAttrsDest>(new SourceWithAttrs { Name = null }).Name);
    }

    [Fact]
    public void AttributeOnSourceMemberIgnore()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(t => Activator.CreateInstance(t)!);
            cfg.CreateMap<SourceWithAttrs, SourceWithAttrsDest>();
        }).CreateMapper();
        Assert.Null(mapper.Map<SourceWithAttrsDest>(new SourceWithAttrs { Secret = "shh" }).Secret);
    }

    [Fact]
    public void AttributeOnSourceMemberResolver()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(t => Activator.CreateInstance(t)!);
            cfg.CreateMap<SourceWithAttrs, SourceWithAttrsDest>();
        }).CreateMapper();
        Assert.Equal("code-X9", mapper.Map<SourceWithAttrsDest>(new SourceWithAttrs { Code = "X9" }).Code);
    }

    [Fact]
    public void DestinationAttributeBeatsSourceAttribute()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceWithAttrs, DestBeatsSourceDest>();
        }).CreateMapper();
        Assert.Equal("from-dest", mapper.Map<DestBeatsSourceDest>(new SourceWithAttrs { Name = null }).Name);
    }
}
