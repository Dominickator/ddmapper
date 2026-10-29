using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Collections: lists, arrays, enumerables, nested element mapping,
/// null collections, and the AllowNullCollections flag.</summary>
public class CollectionTests
{
    public class Item
    {
        public int Id { get; set; }
        public string? Label { get; set; }
    }

    public class ItemDto
    {
        public int Id { get; set; }
        public string? Label { get; set; }
    }

    public class Basket
    {
        public List<int> Scores { get; set; } = new();
        public int[]? Codes { get; set; }
        public List<Item>? Items { get; set; }
        public IEnumerable<int>? Values { get; set; }
        public List<List<int>>? Grid { get; set; }
    }

    public class BasketDto
    {
        public List<int> Scores { get; set; } = new();
        public int[]? Codes { get; set; }
        public List<ItemDto>? Items { get; set; }
        public IEnumerable<int>? Values { get; set; }
        public List<List<int>>? Grid { get; set; }
    }

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.CreateMap<Basket, BasketDto>();
        cfg.CreateMap<Item, ItemDto>();
    }).CreateMapper();

    [Fact]
    public void ListToListCopiesElements()
    {
        var dest = Mapper().Map<BasketDto>(new Basket { Scores = new List<int> { 3, 1, 2 } });
        Assert.Equal(new[] { 3, 1, 2 }, dest.Scores);
    }

    [Fact]
    public void ListToArrayMaterializesArray()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Basket, BasketDto>().ForMember(d => d.Codes, o => o.MapFrom(s => s.Scores))
        ).CreateMapper();
        var dest = mapper.Map<BasketDto>(new Basket { Scores = new List<int> { 4, 5 } });
        Assert.NotNull(dest.Codes);
        Assert.Equal(new[] { 4, 5 }, dest.Codes);
        Assert.IsType<int[]>(dest.Codes);
    }

    [Fact]
    public void ArrayToList()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Basket, BasketDto>().ForMember(d => d.Scores, o => o.MapFrom(s => s.Codes))
        ).CreateMapper();
        var dest = mapper.Map<BasketDto>(new Basket { Codes = new[] { 8, 9 } });
        Assert.Equal(new[] { 8, 9 }, dest.Scores);
    }

    [Fact]
    public void ElementMappingAppliesNestedMap()
    {
        var dest = Mapper().Map<BasketDto>(new Basket
        {
            Items = new List<Item> { new() { Id = 1, Label = "a" }, new() { Id = 2, Label = "b" } }
        });
        Assert.NotNull(dest.Items);
        Assert.Equal(2, dest.Items!.Count);
        Assert.Equal(1, dest.Items[0].Id);
        Assert.Equal("b", dest.Items[1].Label);
        Assert.IsType<ItemDto>(dest.Items[0]);
    }

    [Fact]
    public void EnumerableDestinationReceivesList()
    {
        var dest = Mapper().Map<BasketDto>(new Basket { Values = new[] { 10, 20 } });
        Assert.NotNull(dest.Values);
        Assert.Equal(new[] { 10, 20 }, dest.Values);
    }

    [Fact]
    public void NestedCollectionsMapRecursively()
    {
        var dest = Mapper().Map<BasketDto>(new Basket { Grid = new List<List<int>> { new() { 1 }, new() { 2, 3 } } });
        Assert.NotNull(dest.Grid);
        Assert.Equal(new[] { 1 }, dest.Grid![0]);
        Assert.Equal(new[] { 2, 3 }, dest.Grid[1]);
    }

    [Fact]
    public void EmptyCollectionMapsToEmpty()
    {
        var dest = Mapper().Map<BasketDto>(new Basket { Scores = new List<int>(), Items = new List<Item>() });
        Assert.Empty(dest.Scores);
        Assert.NotNull(dest.Items);
        Assert.Empty(dest.Items!);
    }

    [Fact]
    public void NullCollectionMapsToNullByDefault()
    {
        var dest = Mapper().Map<BasketDto>(new Basket { Items = null });
        Assert.Null(dest.Items);
    }

    [Fact]
    public void NullCollectionBecomesEmptyWhenFlagIsOff()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AllowNullCollections = false;
            cfg.CreateMap<Basket, BasketDto>();
        }).CreateMapper();
        var dest = mapper.Map<BasketDto>(new Basket { Items = null });
        Assert.NotNull(dest.Items);
        Assert.Empty(dest.Items!);
    }

    [Fact]
    public void DirectCollectionMapWithoutTypeMap()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var dest = mapper.Map<List<string>>(new List<int> { 1, 2 });
        Assert.Equal(new[] { "1", "2" }, dest);
        var array = mapper.Map<int[]>(new List<int> { 7, 8 });
        Assert.Equal(new[] { 7, 8 }, array);
    }
}
