using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Generics: closed-generic pairs and open-generic closing
/// (conventional matching only — custom expressions recorded against open
/// type parameters are a documented Stage 1 limit and are not tested here).</summary>
public class GenericTests
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

    [Fact]
    public void ClosedGenericPairMaps()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Wrapper<int>, WrapperDto<int>>()
        ).CreateMapper();
        var dest = mapper.Map<WrapperDto<int>>(new Wrapper<int> { Value = 7, Label = "seven" });
        Assert.Equal(7, dest.Value);
        Assert.Equal("seven", dest.Label);
    }

    [Fact]
    public void ClosedGenericPairWithReferenceElementMaps()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Wrapper<string>, WrapperDto<string>>();
            cfg.CreateMap<Inner, InnerDto>();
        }).CreateMapper();
        var dest = mapper.Map<WrapperDto<string>>(new Wrapper<string> { Value = "hi", Label = "greeting" });
        Assert.Equal("hi", dest.Value);
    }

    public class Inner
    {
        public string? Text { get; set; }
    }

    public class InnerDto
    {
        public string? Text { get; set; }
    }

    [Fact]
    public void OpenGenericMapClosesOnDemand()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap(typeof(Wrapper<>), typeof(WrapperDto<>))
        ).CreateMapper();
        var intDest = mapper.Map<WrapperDto<int>>(new Wrapper<int> { Value = 3, Label = "three" });
        Assert.Equal(3, intDest.Value);
        Assert.Equal("three", intDest.Label);
        var stringDest = mapper.Map<WrapperDto<string>>(new Wrapper<string> { Value = "s", Label = "str" });
        Assert.Equal("s", stringDest.Value);
        Assert.Equal("str", stringDest.Label);
    }

    [Fact]
    public void ClosedGenericPairMapsNestedElement()
    {
        // The open-generic map closes source and destination over the SAME arguments,
        // so a changed element type needs a closed pair: Wrapper<Inner> -> WrapperDto<InnerDto>.
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Wrapper<Inner>, WrapperDto<InnerDto>>();
            cfg.CreateMap<Inner, InnerDto>();
        }).CreateMapper();
        var dest = mapper.Map<WrapperDto<InnerDto>>(new Wrapper<Inner> { Value = new Inner { Text = "nested" }, Label = "wrap" });
        Assert.NotNull(dest.Value);
        Assert.IsType<InnerDto>(dest.Value);
        Assert.Equal("nested", dest.Value!.Text);
    }

    [Fact]
    public void OpenGenericMapClosesInsideCollections()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap(typeof(Wrapper<>), typeof(WrapperDto<>))
        ).CreateMapper();
        var dest = mapper.Map<List<WrapperDto<int>>>(new List<Wrapper<int>>
        {
            new() { Value = 1, Label = "one" },
            new() { Value = 2, Label = "two" },
        });
        Assert.Equal(2, dest.Count);
        Assert.Equal(1, dest[0].Value);
        Assert.Equal("two", dest[1].Label);
    }

    [Fact]
    public void DifferentClosedPairsFromOneOpenMapAreIndependent()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap(typeof(Wrapper<>), typeof(WrapperDto<>))
        ).CreateMapper();
        Assert.Equal(10, mapper.Map<WrapperDto<int>>(new Wrapper<int> { Value = 10 }).Value);
        Assert.Equal(2.5, mapper.Map<WrapperDto<double>>(new Wrapper<double> { Value = 2.5 }).Value);
    }
}
