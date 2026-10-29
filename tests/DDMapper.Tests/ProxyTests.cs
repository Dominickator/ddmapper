using System.ComponentModel;
using AutoMapper;
using AutoMapper.Execution;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Stage 2: runtime proxy generation for interface and abstract destinations.</summary>
public class ProxyTests
{
    public class PersonSrc
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public interface IPerson
    {
        int Id { get; set; }
        string? Name { get; set; }
    }

    [Fact]
    public void SimpleInterfaceDestinationMaps()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IPerson>()).CreateMapper();
        var result = mapper.Map<IPerson>(new PersonSrc { Id = 7, Name = "Ada" });
        Assert.Equal(7, result.Id);
        Assert.Equal("Ada", result.Name);
        Assert.IsAssignableFrom<IPerson>(result);
        Assert.NotEqual(typeof(IPerson), result.GetType());
    }

    [Fact]
    public void ProxyImplementsNotifyPropertyChangedEvenWhenInterfaceDoesNot()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IPerson>()).CreateMapper();
        var result = mapper.Map<IPerson>(new PersonSrc { Id = 1, Name = "Bo" });
        var notifying = Assert.IsAssignableFrom<INotifyPropertyChanged>(result);
        var raised = new List<string>();
        notifying.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        result.Name = "Cy";
        Assert.Contains("Name", raised);
        result.Id = 2;
        Assert.Contains("Id", raised);
    }

    public class InnerSrc
    {
        public string? City { get; set; }
    }

    public interface IInner
    {
        string? City { get; set; }
    }

    public class OuterSrc
    {
        public int Id { get; set; }
        public InnerSrc? Inner { get; set; }
    }

    public interface IOuter
    {
        int Id { get; set; }
        IInner? Inner { get; set; }
    }

    [Fact]
    public void NestedInterfacePropertiesMapWithExplicitMaps()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<OuterSrc, IOuter>();
            cfg.CreateMap<InnerSrc, IInner>();
        }).CreateMapper();
        var result = mapper.Map<IOuter>(new OuterSrc { Id = 3, Inner = new InnerSrc { City = "Leeds" } });
        Assert.Equal(3, result.Id);
        Assert.NotNull(result.Inner);
        Assert.Equal("Leeds", result.Inner!.City);
        Assert.IsAssignableFrom<IInner>(result.Inner);
    }

    [Fact]
    public void NestedInterfacePropertiesMapWithoutExplicitMaps()
    {
        // No CreateMap at all: the proxy fallback copies matching members by name.
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var result = mapper.Map<OuterSrc, IOuter>(new OuterSrc { Id = 4, Inner = new InnerSrc { City = "York" } });
        Assert.Equal(4, result.Id);
        Assert.NotNull(result.Inner);
        Assert.Equal("York", result.Inner!.City);
    }

    public class HolderSrc
    {
        public List<InnerSrc> Items { get; set; } = new();
    }

    public interface IHolder
    {
        List<IInner> Items { get; set; }
    }

    [Fact]
    public void InterfaceCollectionsMap()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<HolderSrc, IHolder>();
            cfg.CreateMap<InnerSrc, IInner>();
        }).CreateMapper();
        var result = mapper.Map<IHolder>(new HolderSrc
        {
            Items = new List<InnerSrc> { new() { City = "A" }, new() { City = "B" } },
        });
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("A", result.Items[0].City);
        Assert.Equal("B", result.Items[1].City);
        Assert.IsAssignableFrom<IInner>(result.Items[0]);
    }

    [Fact]
    public void InterfaceCollectionElementsMapWithoutExplicitElementMap()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var result = mapper.Map<List<IInner>>(new List<InnerSrc>
        {
            new() { City = "X" },
            new() { City = "Y" },
        });
        Assert.Equal(2, result.Count);
        Assert.Equal("X", result[0].City);
        Assert.Equal("Y", result[1].City);
    }

    [Fact]
    public void ProxyTypeIsCachedPerInterface()
    {
        Assert.Same(ProxyGenerator.GetProxyType(typeof(IPerson)), ProxyGenerator.GetProxyType(typeof(IPerson)));
        var first = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IPerson>()).CreateMapper()
            .Map<IPerson>(new PersonSrc { Id = 1 });
        var second = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IPerson>()).CreateMapper()
            .Map<IPerson>(new PersonSrc { Id = 2 });
        Assert.Same(first.GetType(), second.GetType());
    }

    public class BoxSrc
    {
        public int Value { get; set; }
    }

    public interface IBox<T>
    {
        T Value { get; set; }
    }

    [Fact]
    public void ClosedGenericInterfaceDestinationMaps()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<BoxSrc, IBox<int>>()).CreateMapper();
        var result = mapper.Map<IBox<int>>(new BoxSrc { Value = 42 });
        Assert.Equal(42, result.Value);
        Assert.NotSame(ProxyGenerator.GetProxyType(typeof(IBox<int>)), ProxyGenerator.GetProxyType(typeof(IBox<string>)));
    }

    public interface IBase
    {
        int Id { get; set; }
    }

    public interface IDerived : IBase
    {
        string? Name { get; set; }
    }

    [Fact]
    public void InheritedInterfacePropertiesMap()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IDerived>()).CreateMapper();
        var result = mapper.Map<IDerived>(new PersonSrc { Id = 9, Name = "Dee" });
        Assert.Equal(9, result.Id);
        Assert.Equal("Dee", result.Name);
    }

    public interface INotifyingPerson : INotifyPropertyChanged
    {
        int Id { get; set; }
        string? Name { get; set; }
    }

    [Fact]
    public void InterfaceExtendingNotifyPropertyChangedMapsAndNotifies()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, INotifyingPerson>()).CreateMapper();
        var result = mapper.Map<INotifyingPerson>(new PersonSrc { Id = 5, Name = "Eli" });
        Assert.Equal("Eli", result.Name);
        var raised = new List<string>();
        ((INotifyPropertyChanged)result).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        result.Name = "Fay";
        Assert.Contains("Name", raised);
    }

    public abstract class AbstractPerson
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void AbstractClassDestinationMaps()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, AbstractPerson>()).CreateMapper();
        var result = mapper.Map<AbstractPerson>(new PersonSrc { Id = 11, Name = "Gus" });
        Assert.Equal(11, result.Id);
        Assert.Equal("Gus", result.Name);
        Assert.NotEqual(typeof(AbstractPerson), result.GetType());
        Assert.IsAssignableFrom<INotifyPropertyChanged>(result);
    }

    public class ShapeSrc
    {
        public int Sides { get; set; }
        public string? Label { get; set; }
    }

    public abstract class AbstractShape
    {
        public abstract int Sides { get; set; }
        public string? Label { get; set; }
    }

    [Fact]
    public void AbstractClassWithAbstractPropertyMaps()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<ShapeSrc, AbstractShape>()).CreateMapper();
        var result = mapper.Map<AbstractShape>(new ShapeSrc { Sides = 6, Label = "Hex" });
        Assert.Equal(6, result.Sides);
        Assert.Equal("Hex", result.Label);
        var raised = new List<string>();
        ((INotifyPropertyChanged)result).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        result.Sides = 8;
        Assert.Contains("Sides", raised);
    }

    public interface IHasMethod
    {
        int Id { get; set; }
        int Compute();
    }

    [Fact]
    public void InterfaceWithMethodIsUnproxyable()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IHasMethod>()).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<IHasMethod>(new PersonSrc { Id = 1 }));
        Assert.Contains("Compute", ex.Message);
        Assert.Contains("proxy", ex.Message);
    }

    public interface IHasEvent
    {
        int Id { get; set; }
        event EventHandler? Changed;
    }

    [Fact]
    public void InterfaceWithNonPropertyChangedEventIsUnproxyable()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, IHasEvent>()).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<IHasEvent>(new PersonSrc { Id = 1 }));
        Assert.Contains("Changed", ex.Message);
    }

    public abstract class AbstractWorker
    {
        public int Id { get; set; }
        public abstract void DoWork();
    }

    [Fact]
    public void AbstractClassWithAbstractMethodIsUnproxyable()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, AbstractWorker>()).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<AbstractWorker>(new PersonSrc { Id = 1 }));
        Assert.Contains("DoWork", ex.Message);
    }

    public abstract class AbstractNoDefault
    {
        public AbstractNoDefault(int id)
        {
            Id = id;
        }

        public int Id { get; set; }
    }

    [Fact]
    public void AbstractClassWithoutParameterlessConstructorIsUnproxyable()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<PersonSrc, AbstractNoDefault>()).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<AbstractNoDefault>(new PersonSrc { Id = 1 }));
        Assert.Contains("parameterless", ex.Message);
    }

    public interface IIndexer
    {
        int this[int index] { get; set; }
    }

    public class IndexSrc
    {
        public int Value { get; set; }
    }

    [Fact]
    public void InterfaceWithIndexerIsUnproxyable()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<IndexSrc, IIndexer>()).CreateMapper();
        Assert.Throws<AutoMapperMappingException>(() => mapper.Map<IIndexer>(new IndexSrc()));
    }

    [Fact]
    public void SourceAlreadyImplementingInterfaceIsReturnedAsIs()
    {
        // Stage 1 assignable behaviour survives the proxy fallback: no copy is made.
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var person = new PersonImpl { Id = 21, Name = "Hal" };
        var result = mapper.Map<IPerson>(person);
        Assert.Same(person, result);
    }

    public class PersonImpl : IPerson
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
