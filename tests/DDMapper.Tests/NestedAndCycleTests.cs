using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Nested objects, reference preservation, cycles, and MaxDepth.</summary>
public class NestedAndCycleTests
{
    public class Inner
    {
        public string? Value { get; set; }
    }

    public class InnerDto
    {
        public string? Value { get; set; }
    }

    public class Outer
    {
        public int Id { get; set; }
        public Inner? Child { get; set; }
    }

    public class OuterDto
    {
        public int Id { get; set; }
        public InnerDto? Child { get; set; }
    }

    [Fact]
    public void NestedObjectUsesNestedMap()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Outer, OuterDto>();
            cfg.CreateMap<Inner, InnerDto>();
        }).CreateMapper();
        var dest = mapper.Map<OuterDto>(new Outer { Id = 1, Child = new Inner { Value = "deep" } });
        Assert.Equal(1, dest.Id);
        Assert.NotNull(dest.Child);
        Assert.IsType<InnerDto>(dest.Child);
        Assert.Equal("deep", dest.Child!.Value);
    }

    [Fact]
    public void NullNestedObjectStaysNull()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Outer, OuterDto>();
            cfg.CreateMap<Inner, InnerDto>();
        }).CreateMapper();
        Assert.Null(mapper.Map<OuterDto>(new Outer { Child = null }).Child);
    }

    public class Node
    {
        public string? Name { get; set; }
        public Node? Left { get; set; }
        public Node? Right { get; set; }
        public Node? Self { get; set; }
    }

    public class NodeDto
    {
        public string? Name { get; set; }
        public NodeDto? Left { get; set; }
        public NodeDto? Right { get; set; }
        public NodeDto? Self { get; set; }
    }

    private static IMapper PreservingMapper() => new MapperConfiguration(cfg =>
        cfg.CreateMap<Node, NodeDto>().PreserveReferences()
    ).CreateMapper();

    private static IMapper PlainMapper() => new MapperConfiguration(cfg =>
        cfg.CreateMap<Node, NodeDto>()
    ).CreateMapper();

    [Fact]
    public void PreserveReferencesSharesOneDestinationForSharedSource()
    {
        var shared = new Node { Name = "shared" };
        var root = new Node { Name = "root", Left = shared, Right = shared };
        var dest = PreservingMapper().Map<NodeDto>(root);
        Assert.NotNull(dest.Left);
        Assert.Same(dest.Left, dest.Right);
        Assert.Equal("shared", dest.Left!.Name);
    }

    [Fact]
    public void WithoutPreserveReferencesSharedSourceMapsTwice()
    {
        var shared = new Node { Name = "shared" };
        var root = new Node { Name = "root", Left = shared, Right = shared };
        var dest = PlainMapper().Map<NodeDto>(root);
        Assert.NotNull(dest.Left);
        Assert.NotNull(dest.Right);
        Assert.NotSame(dest.Left, dest.Right);
        Assert.Equal(dest.Left!.Name, dest.Right!.Name);
    }

    [Fact]
    public void PreserveReferencesResolvesSelfCycle()
    {
        var node = new Node { Name = "loop" };
        node.Self = node;
        var dest = PreservingMapper().Map<NodeDto>(node);
        Assert.Same(dest, dest.Self);
    }

    [Fact]
    public void CycleWithoutPreserveReferencesStopsAtNull()
    {
        var node = new Node { Name = "loop" };
        node.Self = node;
        var dest = PlainMapper().Map<NodeDto>(node);
        Assert.Null(dest.Self);
    }

    [Fact]
    public void MaxDepthOneCutsFirstNestingLevel()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Node, NodeDto>().MaxDepth(1)
        ).CreateMapper();
        var dest = mapper.Map<NodeDto>(new Node { Name = "a", Left = new Node { Name = "b" } });
        Assert.Equal("a", dest.Name);
        Assert.Null(dest.Left);
    }

    [Fact]
    public void MaxDepthTwoKeepsOneNestingLevel()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Node, NodeDto>().MaxDepth(2)
        ).CreateMapper();
        var dest = mapper.Map<NodeDto>(new Node
        {
            Name = "a",
            Left = new Node { Name = "b", Left = new Node { Name = "c" } }
        });
        Assert.NotNull(dest.Left);
        Assert.Equal("b", dest.Left!.Name);
        Assert.Null(dest.Left.Left);
    }

    [Fact]
    public void DeepChainWithoutMaxDepthTerminatesOnNullLeaves()
    {
        var leaf = new Node { Name = "leaf" };
        var mid = new Node { Name = "mid", Left = leaf };
        var root = new Node { Name = "root", Left = mid };
        var dest = PlainMapper().Map<NodeDto>(root);
        Assert.Equal("leaf", dest.Left!.Left!.Name);
        Assert.Null(dest.Left.Left.Left);
    }
}
