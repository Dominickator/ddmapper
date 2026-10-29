using System.Linq.Expressions;
using System.Reflection;
using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 4 generics edges: open-generic closing with hooks,
/// converters, and transformers preserved; generic collections; nested
/// generics; generic interface destinations via proxies; generic
/// derived-type dispatch; and replay of custom member expressions
/// recorded against open generic definitions.</summary>
public class GenericsEdgeTests
{
    public class Wrapper<T>
    {
        public T? Value { get; set; }
        public string? Label { get; set; }
    }

    public class WrapperDto<T>
    {
        public T? Value { get; set; }
        public string? Label { get; set; }
    }

    public class Inner
    {
        public string? Text { get; set; }
    }

    public class InnerDto
    {
        public string? Text { get; set; }
    }

    public interface IBox<T>
    {
        T? Value { get; set; }
        string? Label { get; set; }
    }

    public class Animal
    {
        public string? Name { get; set; }
    }

    public class Dog : Animal
    {
        public string? Breed { get; set; }
    }

    public class AnimalDto
    {
        public string? Name { get; set; }
    }

    public class DogDto : AnimalDto
    {
        public string? Breed { get; set; }
    }

    [Fact]
    public void OpenGenericClosingPreservesHooksAndTransformers()
    {
        var seen = new List<string>();
        var mapper = new MapperConfiguration(cfg =>
        {
            var open = (NonGenericMappingExpression)cfg.CreateMap(typeof(Wrapper<>), typeof(WrapperDto<>));
            open.BeforeMap((s, d) => seen.Add("before"));
            open.AddValueTransformer<string>(v => v + "!");
        }).CreateMapper();
        var dest = mapper.Map<WrapperDto<int>>(new Wrapper<int> { Value = 3, Label = "three" });
        Assert.Equal("three!", dest.Label);
        Assert.Equal(3, dest.Value);
        Assert.Equal(new[] { "before" }, seen);
    }

    [Fact]
    public void OpenGenericClosingPreservesWholeTypeConverter()
    {
        var mapper = new MapperConfiguration(cfg =>
            ((NonGenericMappingExpression)cfg.CreateMap(typeof(Wrapper<>), typeof(WrapperDto<>)))
                .ConvertUsing(s =>
                {
                    // Build the closed DTO from the closed source by reflection.
                    var sourceType = s.GetType();
                    var arg = sourceType.GetGenericArguments()[0];
                    var dtoType = typeof(WrapperDto<>).MakeGenericType(arg);
                    var dto = Activator.CreateInstance(dtoType)!;
                    dtoType.GetProperty("Value")!.SetValue(dto,
                        sourceType.GetProperty("Value")!.GetValue(s));
                    dtoType.GetProperty("Label")!.SetValue(dto,
                        (sourceType.GetProperty("Label")!.GetValue(s) as string) + "?");
                    return dto;
                })).CreateMapper();
        var dest = mapper.Map<WrapperDto<string>>(new Wrapper<string> { Value = "v", Label = "l" });
        Assert.Equal("v", dest.Value);
        Assert.Equal("l?", dest.Label);
    }

    [Fact]
    public void GenericCollectionElementMapping()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Wrapper<Inner>, WrapperDto<InnerDto>>();
            cfg.CreateMap<Inner, InnerDto>();
        }).CreateMapper();
        var dest = mapper.Map<List<WrapperDto<InnerDto>>>(new List<Wrapper<Inner>>
        {
            new() { Value = new Inner { Text = "one" }, Label = "a" },
            new() { Value = new Inner { Text = "two" }, Label = "b" },
        });
        Assert.Equal(2, dest.Count);
        Assert.Equal("one", dest[0].Value!.Text);
        Assert.Equal("two", dest[1].Value!.Text);
    }

    [Fact]
    public void NestedGenericsMap()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Wrapper<Wrapper<int>>, WrapperDto<WrapperDto<int>>>();
            cfg.CreateMap<Wrapper<int>, WrapperDto<int>>();
        }).CreateMapper();
        var dest = mapper.Map<WrapperDto<WrapperDto<int>>>(
            new Wrapper<Wrapper<int>> { Value = new Wrapper<int> { Value = 42, Label = "inner" }, Label = "outer" });
        Assert.Equal("outer", dest.Label);
        Assert.NotNull(dest.Value);
        Assert.Equal(42, dest.Value!.Value);
        Assert.Equal("inner", dest.Value.Label);
    }

    [Fact]
    public void GenericInterfaceDestinationViaProxy()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Wrapper<int>, IBox<int>>()).CreateMapper();
        var dest = mapper.Map<IBox<int>>(new Wrapper<int> { Value = 9, Label = "boxed" });
        Assert.Equal(9, dest.Value);
        Assert.Equal("boxed", dest.Label);
    }

    [Fact]
    public void GenericDerivedDispatchMapsDerivedMembers()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Wrapper<Animal>, WrapperDto<AnimalDto>>()
                .Include<Wrapper<Dog>, WrapperDto<DogDto>>();
            cfg.CreateMap<Wrapper<Dog>, WrapperDto<DogDto>>();
            cfg.CreateMap<Animal, AnimalDto>();
            cfg.CreateMap<Dog, DogDto>();
        }).CreateMapper();
        object source = new Wrapper<Dog> { Value = new Dog { Name = "Rex", Breed = "Lab" }, Label = "dog" };
        var dest = (WrapperDto<DogDto>)mapper.Map(source, typeof(Wrapper<Animal>), typeof(WrapperDto<DogDto>));
        Assert.Equal("dog", dest.Label);
        Assert.NotNull(dest.Value);
        Assert.Equal("Rex", dest.Value!.Name);
        Assert.Equal("Lab", dest.Value.Breed);
    }

    [Fact]
    public void OpenGenericCustomExpressionsCannotBeRecorded()
    {
        // Documented Stage 4 limit, resolved as "documented with reason"
        // rather than implemented: .NET expression trees cannot hold a
        // lambda over an open generic type at all — Expression.Parameter
        // rejects both a generic definition and any type containing a
        // generic parameter (verified against the framework). The public
        // open-map handle also exposes no per-member surface, so no
        // custom member expression can ever be recorded on an open map.
        // Closed maps therefore rely on conventional matching plus the
        // inherited map-level state (hooks, converters, transformers),
        // and never silently carry a stale custom expression.
        Assert.Throws<ArgumentException>(() => Expression.Parameter(typeof(Wrapper<>), "src"));
        var tParam = typeof(GenericsEdgeTests).GetMethod(nameof(Identity))!.GetGenericArguments()[0];
        Assert.Throws<ArgumentException>(() =>
            Expression.Parameter(typeof(Wrapper<>).MakeGenericType(tParam), "src"));
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap(typeof(Wrapper<>), typeof(WrapperDto<>))).CreateMapper();
        var dest = mapper.Map<WrapperDto<int>>(new Wrapper<int> { Value = 5, Label = "plain" });
        Assert.Equal("plain", dest.Label);
        Assert.Equal(5, dest.Value);
    }

    public static T Identity<T>(T value) => value;
    public static T Other<T>(T value) => value;
}
