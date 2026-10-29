using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Sealed configuration, missing maps, and unsupported destinations.</summary>
public class SealedAndMissingMapTests
{
    public class A
    {
        public int Id { get; set; }
    }

    public class B
    {
        public int Id { get; set; }
    }

    public class C
    {
        public int Id { get; set; }
    }

    [Fact]
    public void CreateMapAfterBuildThrows()
    {
        IMapperConfigurationExpression? captured = null;
        _ = new MapperConfiguration(cfg =>
        {
            captured = cfg;
            cfg.CreateMap<A, B>();
        });
        Assert.NotNull(captured);
        var ex = Assert.Throws<AutoMapperConfigurationException>(() => captured!.CreateMap<B, C>());
        Assert.Contains("already been built", ex.Message);
    }

    [Fact]
    public void ForMemberAfterBuildThrows()
    {
        IMappingExpression<A, B>? captured = null;
        _ = new MapperConfiguration(cfg => { captured = cfg.CreateMap<A, B>(); });
        Assert.NotNull(captured);
        var ex = Assert.Throws<AutoMapperConfigurationException>(() =>
            captured!.ForMember(d => d.Id, o => o.Ignore()));
        Assert.Contains("already been built", ex.Message);
    }

    [Fact]
    public void MissingMapThrowsMappingException()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<B>(new A()));
        Assert.Contains("No map is configured", ex.Message);
        Assert.Contains("A", ex.Message);
        Assert.Contains("B", ex.Message);
    }

    [Fact]
    public void MissingMapThrowsThroughNonGenericOverload()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        Assert.Throws<AutoMapperMappingException>(() => mapper.Map(new A(), typeof(A), typeof(B)));
    }

    public interface IDest
    {
        int Id { get; set; }
    }

    // STAGE 2: superseded by proxy support — interface destinations now map
    // through a runtime proxy instead of throwing.
    [Fact]
    public void InterfaceDestinationMapsThroughProxy()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<A, IDest>()).CreateMapper();
        var result = mapper.Map<IDest>(new A { Id = 5 });
        Assert.Equal(5, result.Id);
        Assert.NotEqual(typeof(IDest), result.GetType());
    }

    public abstract class AbstractDest
    {
        public int Id { get; set; }
    }

    // STAGE 2: superseded by proxy support — abstract destinations now map
    // through a runtime proxy subclass instead of throwing.
    [Fact]
    public void AbstractDestinationMapsThroughProxy()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<A, AbstractDest>()).CreateMapper();
        var result = mapper.Map<AbstractDest>(new A { Id = 6 });
        Assert.Equal(6, result.Id);
        Assert.NotEqual(typeof(AbstractDest), result.GetType());
    }
}
