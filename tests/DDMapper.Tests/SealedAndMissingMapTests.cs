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

    [Fact]
    public void InterfaceDestinationThrowsMappingException()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<A, IDest>()).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<IDest>(new A()));
        Assert.Contains("interface", ex.Message);
    }

    public abstract class AbstractDest
    {
        public int Id { get; set; }
    }

    [Fact]
    public void AbstractDestinationThrowsMappingException()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<A, AbstractDest>()).CreateMapper();
        Assert.Throws<AutoMapperMappingException>(() => mapper.Map<AbstractDest>(new A()));
    }
}
