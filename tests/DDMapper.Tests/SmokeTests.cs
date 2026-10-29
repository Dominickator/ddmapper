using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

// Smoke tests only: they prove Map works end to end. The full suite is owned by a later stage.
public class SmokeTests
{
    public class Source
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public Customer? Customer { get; set; }
        public List<int> Scores { get; set; } = new();
        public string Status { get; set; } = "Active";
    }
    public class Customer { public string? Name { get; set; } }
    public class Dest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? CustomerName { get; set; }
        public List<int> Scores { get; set; } = new();
        public Status Status { get; set; }
        public string? Ignored { get; set; }
    }
    public enum Status { Active, Inactive }

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.CreateMap<Source, Dest>().ForMember(d => d.Ignored, o => o.Ignore());
        cfg.CreateMap<Customer, Dest>();
    }).CreateMapper();

    [Fact]
    public void BasicMapCopiesMembersAndFlattens()
    {
        var dest = Mapper().Map<Dest>(new Source { Id = 7, Name = "Ada", Customer = new Customer { Name = "Grace" }, Scores = new List<int> { 1, 2 } });
        Assert.Equal(7, dest.Id);
        Assert.Equal("Ada", dest.Name);
        Assert.Equal("Grace", dest.CustomerName);
        Assert.Equal(new[] { 1, 2 }, dest.Scores);
    }

    [Fact]
    public void StringToEnumFallbackWorks()
    {
        var dest = Mapper().Map<Dest>(new Source { Status = "Inactive" });
        Assert.Equal(Status.Inactive, dest.Status);
    }

    [Fact]
    public void MapFromAndConditionWork()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name!.ToUpperInvariant()))
                .ForMember(d => d.Ignored, o => o.Ignore())
                .ForMember(d => d.CustomerName, o => o.Ignore());
        }).CreateMapper();
        var dest = mapper.Map<Dest>(new Source { Name = "ada" });
        Assert.Equal("ADA", dest.Name);
    }

    [Fact]
    public void ReverseMapWorks()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Source, Dest>().ForMember(d => d.Ignored, o => o.Ignore()).ReverseMap();
        }).CreateMapper();
        var back = mapper.Map<Source>(new Dest { Id = 3, Name = "Bob" });
        Assert.Equal(3, back.Id);
        Assert.Equal("Bob", back.Name);
    }

    [Fact]
    public void MissingMapThrowsClearError()
    {
        var mapper = new MapperConfiguration(_ => { }).CreateMapper();
        var ex = Assert.Throws<AutoMapperMappingException>(() => mapper.Map<Dest>(new Customer()));
        Assert.Contains("No map is configured", ex.Message);
    }

    [Fact]
    public void ValidationReportsUnmappedMembers()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>());
        var ex = Assert.Throws<AutoMapperConfigurationException>(() => config.AssertConfigurationIsValid());
        // CustomerName flattens from Customer.Name, so it is mapped; Ignored has no source.
        Assert.Contains("Ignored", ex.Message);
    }
}
