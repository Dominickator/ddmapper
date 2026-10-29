using AutoMapper;
using System.Dynamic;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Fallback chain spot-checks: assignable passthrough, IConvertible,
/// dictionary mapping, dynamic/ExpandoObject both directions, string
/// dictionaries, and conversion operators.</summary>
public class FallbackTests
{
    public class Base
    {
        public int Id { get; set; }
    }

    public class Derived : Base
    {
        public string? Extra { get; set; }
    }

    [Fact]
    public void AssignablePassthroughReturnsSameInstance()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var derived = new Derived { Id = 5, Extra = "x" };
        var asBase = mapper.Map<Base>(derived);
        Assert.Same(derived, asBase);
        var asObject = mapper.Map<object>(derived);
        Assert.Same(derived, asObject);
    }

    public class Item
    {
        public int Id { get; set; }
    }

    public class ItemDto
    {
        public int Id { get; set; }
    }

    [Fact]
    public void DictionaryMappingConvertsValues()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var dest = mapper.Map<Dictionary<string, long>>(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        Assert.Equal(1L, dest["a"]);
        Assert.Equal(2L, dest["b"]);
    }

    [Fact]
    public void DictionaryMappingConvertsKeys()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var dest = mapper.Map<Dictionary<string, string>>(new Dictionary<int, string> { [1] = "one", [2] = "two" });
        Assert.Equal("one", dest["1"]);
        Assert.Equal("two", dest["2"]);
    }

    [Fact]
    public void DictionaryMappingUsesElementMap()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Item, ItemDto>()).CreateMapper();
        var dest = mapper.Map<Dictionary<string, ItemDto>>(new Dictionary<string, Item>
        {
            ["first"] = new Item { Id = 7 }
        });
        Assert.IsType<ItemDto>(dest["first"]);
        Assert.Equal(7, dest["first"].Id);
    }

    public class SimpleDest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class SimpleSrc
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void ExpandoObjectMapsToDestination()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;
        dict["Id"] = 4;
        dict["Name"] = "Ada";
        var dest = mapper.Map<SimpleDest>((object)expando);
        Assert.Equal(4, dest.Id);
        Assert.Equal("Ada", dest.Name);
    }

    [Fact]
    public void ObjectMapsToExpandoObject()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var result = mapper.Map<ExpandoObject>(new SimpleSrc { Id = 6, Name = "Bo" });
        var dict = (IDictionary<string, object?>)result;
        Assert.Equal(6, dict["Id"]);
        Assert.Equal("Bo", dict["Name"]);
    }

    [Fact]
    public void StringDictionaryMapsToDestination()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var dest = mapper.Map<SimpleDest>(new Dictionary<string, string> { ["Id"] = "8", ["Name"] = "Cy" });
        Assert.Equal(8, dest.Id);
        Assert.Equal("Cy", dest.Name);
    }

    [Fact]
    public void ObjectMapsToStringDictionary()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var dest = mapper.Map<Dictionary<string, string>>(new SimpleSrc { Id = 3, Name = "Dee" });
        Assert.Equal("3", dest["Id"]);
        Assert.Equal("Dee", dest["Name"]);
    }

    public sealed class Meters
    {
        public double Value { get; set; }
        public static implicit operator Feet(Meters m) => new() { Value = m.Value * 3.28084 };
    }

    public sealed class Feet
    {
        public double Value { get; set; }
    }

    [Fact]
    public void ConversionOperatorIsUsed()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var feet = mapper.Map<Feet>(new Meters { Value = 1 });
        Assert.Equal(3.28084, feet.Value, precision: 5);
    }

    [Fact]
    public void NoMapAndNoConversionThrowsMappingException()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<ItemDto>(new SimpleSrc()));
        Assert.Contains("No map is configured", ex.Message);
    }
}
