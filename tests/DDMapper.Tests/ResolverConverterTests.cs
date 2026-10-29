using AutoMapper;
using AutoMapper.QueryableExtensions;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 4: the full resolver / converter / mapping-action surface
/// on the object-mapping path, plus its projection behaviour (works or a
/// clear AutoMapperProjectionException, never a silent drop).</summary>
public class ResolverConverterTests
{
    public class Src
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Code { get; set; }
        public Inner? Inner { get; set; }
    }

    public class Dst
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Code { get; set; }
        public string? Greeting { get; set; }
        public InnerDto? Inner { get; set; }
    }

    public class Inner
    {
        public string? Text { get; set; }
        public int Number { get; set; }
    }

    public class InnerDto
    {
        public string? Text { get; set; }
        public int Number { get; set; }
    }

    // Receives the destination member value and the context items.
    public class GreetingResolver : IValueResolver<Src, Dst, string?>
    {
        public string? Resolve(Src source, Dst destination, string? destMember, ResolutionContext context) =>
            $"{context.Items["prefix"]}{source.Name} (was: {destMember ?? "none"})";
    }

    // Receives source member, destination member, and context items.
    public class NameMemberResolver : IMemberValueResolver<Src, Dst, string?, string?>
    {
        public string? Resolve(Src source, Dst destination, string? sourceMember, string? destMember, ResolutionContext context) =>
            $"{context.Items["wrap"]}{sourceMember?.ToUpperInvariant()}|{destMember}";
    }

    // Member resolver whose source value comes from a MapFrom expression.
    public class FullMemberResolver : IMemberValueResolver<Src, Dst, string?, string?>
    {
        public string? Resolve(Src source, Dst destination, string? sourceMember, string? destMember, ResolutionContext context) =>
            $"[{sourceMember}]";
    }

    // Service-constructed resolver: the prefix comes from construction.
    public class PrefixResolver : IValueResolver<Src, Dst, string?>
    {
        private readonly string _prefix;
        public PrefixResolver(string prefix) { _prefix = prefix; }
        public string? Resolve(Src source, Dst destination, string? destMember, ResolutionContext context) =>
            _prefix + source.Code;
    }

    // Service-constructed member resolver.
    public class PrefixMemberResolver : IMemberValueResolver<Src, Dst, string?, string?>
    {
        private readonly string _prefix;
        public PrefixMemberResolver(string prefix) { _prefix = prefix; }
        public string? Resolve(Src source, Dst destination, string? sourceMember, string? destMember, ResolutionContext context) =>
            _prefix + sourceMember;
    }

    // Whole-type converter with context (reads items).
    public class WholeConverter : ITypeConverter<Src, Dst>
    {
        public Dst Convert(Src source, Dst? destination, ResolutionContext context) =>
            new() { Name = $"{context.Items["tag"]}:{source.Name}", Age = source.Age };
    }

    // Whole-type converter without a context parameter.
    public class TwoArgConverter
    {
        public Dst Convert(Src source, Dst? destination) =>
            new() { Name = source.Name + "!", Age = source.Age + 1 };
    }

    // Whole-type converter with only a source parameter.
    public class OneArgConverter
    {
        public Dst Convert(Src source) => new() { Name = source.Name, Age = 99 };
    }

    // Member converter (interface shape, with context).
    public class UpperConverter : ITypeConverter<object, string?>
    {
        public string? Convert(object source, string? destination, ResolutionContext context) =>
            source?.ToString()?.ToUpperInvariant();
    }

    // Member value converter (source-member/destination-member shape).
    public class DashValueConverter : IValueConverter<string?, string?>
    {
        public string? Convert(string? sourceMember, string? destMember, ResolutionContext context) =>
            sourceMember == null ? null : sourceMember + "-" + (destMember ?? "none");
    }

    // Member converter without a context parameter, via type-based registration.
    public class BracketConverter
    {
        public string? Convert(object source, string? destination) => "[" + source + "]";
    }

    public class LogAction : IMappingAction<Src, Dst>
    {
        public static List<string> Seen { get; } = new();
        public void Process(Src source, Dst destination, ResolutionContext context) => Seen.Add(source.Name ?? "");
    }

    public class TaggedAction : IMappingAction<Src, Dst>
    {
        private readonly string _tag;
        public TaggedAction(string tag) { _tag = tag; }
        public void Process(Src source, Dst destination, ResolutionContext context) => destination.Greeting = _tag;
    }

    private static IMapper BasicMapper(Action<IMapperConfigurationExpression>? extra = null)
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Src, Dst>();
            cfg.CreateMap<Inner, InnerDto>();
            extra?.Invoke(cfg);
        });
        return config.CreateMapper();
    }

    [Fact]
    public void ValueResolverReceivesDestinationMemberAndContextItems()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Greeting, m => m.MapFrom(new GreetingResolver())));
        var existing = new Dst { Greeting = "old" };
        var dest = mapper.Map(new Src { Name = "Ada" }, existing,
            opts => opts.Items["prefix"] = "hi ");
        Assert.Equal("hi Ada (was: old)", dest.Greeting);
    }

    [Fact]
    public void MemberValueResolverReceivesSourceAndDestinationMembers()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m => m.MapFrom<NameMemberResolver, string?>(new NameMemberResolver())));
        var existing = new Dst { Name = "kept" };
        var dest = mapper.Map(new Src { Name = "bob" }, existing,
            opts => opts.Items["wrap"] = ">>");
        Assert.Equal(">>BOB|kept", dest.Name);
    }

    [Fact]
    public void MemberValueResolverSourceCanComeFromMapFromExpression()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Greeting, m =>
                {
                    m.MapFrom(s => s.Name + "#" + s.Age);
                    m.MapFrom<FullMemberResolver, string?>(new FullMemberResolver());
                }));
        var dest = mapper.Map<Dst>(new Src { Name = "Cy", Age = 4 });
        Assert.Equal("[Cy#4]", dest.Greeting);
    }

    [Fact]
    public void ResolverTypeIsBuiltThroughServiceConstructor()
    {
        var mapper = BasicMapper(cfg =>
        {
            cfg.ConstructServicesUsing(t =>
                t == typeof(PrefixResolver) ? new PrefixResolver("svc-") : Activator.CreateInstance(t)!);
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Code, m => m.MapFrom<PrefixResolver>());
        });
        var dest = mapper.Map<Dst>(new Src { Code = "x9" });
        Assert.Equal("svc-x9", dest.Code);
    }

    [Fact]
    public void MemberResolverTypeIsBuiltThroughServiceConstructor()
    {
        var mapper = BasicMapper(cfg =>
        {
            cfg.ConstructServicesUsing(t =>
                t == typeof(PrefixMemberResolver) ? new PrefixMemberResolver("m-") : Activator.CreateInstance(t)!);
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m => m.MapFromMember<PrefixMemberResolver, string?>());
        });
        var dest = mapper.Map<Dst>(new Src { Name = "ned" });
        Assert.Equal("m-ned", dest.Name);
    }

    [Fact]
    public void WholeTypeConverterInstanceWithContext()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>().ConvertUsing(new WholeConverter()));
        var dest = mapper.Map<Src, Dst>(new Src { Name = "Jo", Age = 3 },
            opts => opts.Items["tag"] = "T");
        Assert.Equal("T:Jo", dest.Name);
        Assert.Equal(3, dest.Age);
    }

    [Fact]
    public void WholeTypeConverterFuncWithoutContext()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>().ConvertUsing(s => new Dst { Name = s.Name, Age = s.Age * 2 }));
        var dest = mapper.Map<Dst>(new Src { Name = "Al", Age = 5 });
        Assert.Equal(10, dest.Age);
    }

    [Fact]
    public void WholeTypeConverterTypeWithoutContextParameter()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>().ConvertUsing<TwoArgConverter>());
        var dest = mapper.Map<Dst>(new Src { Name = "Bo", Age = 1 });
        Assert.Equal("Bo!", dest.Name);
        Assert.Equal(2, dest.Age);
    }

    [Fact]
    public void WholeTypeConverterTypeWithSourceOnlyParameter()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>().ConvertUsing<OneArgConverter>());
        var dest = mapper.Map<Dst>(new Src { Name = "Bo", Age = 1 });
        Assert.Equal(99, dest.Age);
    }

    [Fact]
    public void WholeTypeConverterTypeWithoutServiceConstructorAndWithoutParameterlessCtorFailsClearly()
    {
        Assert.Throws<AutoMapperConfigurationException>(() =>
            new MapperConfiguration(cfg =>
                cfg.CreateMap<Src, Dst>().ConvertUsing<TaggedActionConverter>()));
    }

    public class TaggedActionConverter
    {
        public TaggedActionConverter(string required) { }
        public Dst Convert(Src source, Dst? destination) => new();
    }

    [Fact]
    public void WholeTypeConverterTypeWithoutServiceConstructorButParameterlessWorks()
    {
        // The default service construction falls back to the parameterless
        // constructor; the clear configuration error only fires when that
        // is impossible (see the test above).
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>().ConvertUsing<TwoArgConverter>());
        Assert.Equal("Bo!", mapper.Map<Dst>(new Src { Name = "Bo" }).Name);
    }

    [Fact]
    public void MemberConverterInstanceAppliedAfterResolution()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m => m.ConvertUsing(new UpperConverter())));
        var dest = mapper.Map<Dst>(new Src { Name = "ada" });
        Assert.Equal("ADA", dest.Name);
    }

    [Fact]
    public void ValueConverterAppliedAfterResolutionBeforeAssignment()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Name, m => m.ConvertUsing(new DashValueConverter())));
        var existing = new Dst { Name = "old" };
        var dest = mapper.Map(new Src { Name = "new" }, existing);
        Assert.Equal("new-old", dest.Name);
    }

    [Fact]
    public void MemberConverterTypeBuiltThroughServiceConstructor()
    {
        var mapper = BasicMapper(cfg =>
        {
            cfg.ConstructServicesUsing(t => Activator.CreateInstance(t)!);
            cfg.CreateMap<Src, Dst>()
                .ForMember(d => d.Code, m => m.ConvertUsing<BracketConverter>());
        });
        var dest = mapper.Map<Dst>(new Src { Code = "k" });
        Assert.Equal("[k]", dest.Code);
    }

    [Fact]
    public void ConverterOnNestedMemberIsHonored()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Inner, InnerDto>()
                .ForMember(d => d.Text, m => m.ConvertUsing(new UpperConverter())));
        var dest = mapper.Map<Dst>(new Src { Inner = new Inner { Text = "deep", Number = 2 } });
        Assert.NotNull(dest.Inner);
        Assert.Equal("DEEP", dest.Inner!.Text);
        Assert.Equal(2, dest.Inner.Number);
    }

    [Fact]
    public void ResolverOnNestedMemberReceivesContextItems()
    {
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Inner, InnerDto>()
                .ForMember(d => d.Text, m => m.MapFrom(new InnerTextResolver())));
        var dest = mapper.Map<Inner, InnerDto>(new Inner { Text = "t" },
            opts => opts.Items["suffix"] = "?");
        Assert.Equal("t?", dest.Text);
    }

    public class InnerTextResolver : IValueResolver<Inner, InnerDto, string?>
    {
        public string? Resolve(Inner source, InnerDto destination, string? destMember, ResolutionContext context) =>
            source.Text + context.Items["suffix"];
    }

    [Fact]
    public void MappingActionInstanceAndTypeRunAroundMembers()
    {
        LogAction.Seen.Clear();
        var mapper = BasicMapper(cfg =>
        {
            cfg.ConstructServicesUsing(t =>
                t == typeof(TaggedAction) ? new TaggedAction("tagged") : Activator.CreateInstance(t)!);
            cfg.CreateMap<Src, Dst>()
                .BeforeMap(new LogAction())
                .AfterMap<TaggedAction>();
        });
        var dest = mapper.Map<Dst>(new Src { Name = "Zed" });
        Assert.Contains("Zed", LogAction.Seen);
        Assert.Equal("tagged", dest.Greeting);
    }

    [Fact]
    public void MappingActionRunsForNestedMapsToo()
    {
        LogAction.Seen.Clear();
        var mapper = BasicMapper(cfg =>
            cfg.CreateMap<Src, Dst>().BeforeMap(new LogAction()));
        // The action is registered on the Src->Dst map only; nested Inner
        // maps do not run it, but the top-level map does.
        mapper.Map<Dst>(new Src { Name = "Top", Inner = new Inner { Text = "x" } });
        Assert.Equal(new[] { "Top" }, LogAction.Seen);
    }

    public class DerivedInner : Inner
    {
        public new string? Text { get; set; }
    }

    [Fact]
    public void MapAtRuntimeWorksThroughNestedMaps()
    {
        IMapper BuildMapper(bool atRuntime)
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Src, Dst>();
                var inner = cfg.CreateMap<Inner, InnerDto>();
                if (atRuntime) inner.ForMember(d => d.Text, m => m.MapAtRuntime());
            });
            return config.CreateMapper();
        }
        var source = new Src { Inner = new DerivedInner { Text = "derived" } };
        ((DerivedInner)source.Inner!).GetType().GetProperty("Text")!.SetValue(source.Inner, "derived");
        // Base Text stays null; the hiding member carries "derived".
        var runtime = BuildMapper(atRuntime: true).Map<Dst>(source);
        Assert.Equal("derived", runtime.Inner!.Text);
        var planTime = BuildMapper(atRuntime: false).Map<Dst>(source);
        Assert.NotEqual("derived", planTime.Inner!.Text);
    }

    public class AttrSrc
    {
        public string? Code { get; set; }
    }

    public class AttrDst
    {
        [AutoMapper.Configuration.Annotations.ValueResolver(typeof(AttrCodeResolver))]
        public string? Code { get; set; }
    }

    public class AttrCodeResolver : IValueResolver<AttrSrc, AttrDst, string?>
    {
        public string? Resolve(AttrSrc source, AttrDst destination, string? destMember, ResolutionContext context) =>
            "attr:" + source.Code;
    }

    public class AttrDepSrc
    {
        public string? Code { get; set; }
    }

    public class AttrDepDst
    {
        [AutoMapper.Configuration.Annotations.ValueResolver(typeof(AttrDepResolver))]
        public string? Code { get; set; }
    }

    public class AttrDepResolver : IValueResolver<AttrDepSrc, AttrDepDst, string?>
    {
        private readonly string _prefix;
        public AttrDepResolver(string prefix) { _prefix = prefix; }
        public string? Resolve(AttrDepSrc source, AttrDepDst destination, string? destMember, ResolutionContext context) =>
            _prefix + source.Code;
    }

    [Fact]
    public void AttributeResolverWithoutServiceConstructorUsesDefaultConstruction()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<AttrSrc, AttrDst>()).CreateMapper();
        Assert.Equal("attr:z", mapper.Map<AttrDst>(new AttrSrc { Code = "z" }).Code);
    }

    [Fact]
    public void AttributeResolverWithDependencyUsesRegisteredServiceConstructor()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.ConstructServicesUsing(t =>
                t == typeof(AttrDepResolver) ? new AttrDepResolver("dep-") : Activator.CreateInstance(t)!);
            cfg.CreateMap<AttrDepSrc, AttrDepDst>();
        }).CreateMapper();
        Assert.Equal("dep-z", mapper.Map<AttrDepDst>(new AttrDepSrc { Code = "z" }).Code);
    }

    [Fact]
    public void AttributeResolverWithoutServiceConstructorAndWithoutParameterlessCtorFailsClearly()
    {
        Assert.Throws<AutoMapperConfigurationException>(() =>
            new MapperConfiguration(cfg =>
                cfg.CreateMap<AttrDepSrc, AttrDepDst>()));
    }

    // ---- Projection behaviour per category ----

    private static MapperConfiguration ProjectionConfig(Action<IMapperConfigurationExpression> configure)
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Src, Dst>();
            cfg.CreateMap<Inner, InnerDto>();
            configure(cfg);
        });
        return config;
    }

    [Fact]
    public void ProjectionRejectsValueResolver()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().ForMember(d => d.Greeting, m => m.MapFrom(new GreetingResolver())));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsMemberValueResolver()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().ForMember(d => d.Name, m => m.MapFrom<NameMemberResolver, string?>(new NameMemberResolver())));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsMemberConverter()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().ForMember(d => d.Name, m => m.ConvertUsing(new UpperConverter())));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsValueConverter()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().ForMember(d => d.Name, m => m.ConvertUsing(new DashValueConverter())));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsWholeTypeConverter()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().ConvertUsing(new WholeConverter()));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsMappingAction()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().BeforeMap(new LogAction()));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionRejectsConverterOnNestedMember()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Inner, InnerDto>().ForMember(d => d.Text, m => m.ConvertUsing(new UpperConverter())));
        Assert.Throws<AutoMapperProjectionException>(() =>
            AutoMapper.QueryableExtensions.QueryableExtensions.BuildProjection<Src, Dst>(config));
    }

    [Fact]
    public void ProjectionWorksForConverterExpressedAsTranslatableExpression()
    {
        var config = ProjectionConfig(cfg =>
            cfg.CreateMap<Src, Dst>().ForMember(d => d.Name, m => m.MapFrom(s => s.Name!.ToUpperInvariant())));
        var results = new List<Src> { new() { Name = "ada" } }.AsQueryable()
            .ProjectTo<Src, Dst>(config).ToList();
        Assert.Equal("ADA", results[0].Name);
    }
}
