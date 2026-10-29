using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Convention mapping: matching names, case-insensitivity, fields,
/// snake_case conventions, and prefix/postfix stripping.</summary>
public class ConventionTests
{
    public class SimpleSource
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class SimpleDest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void MatchingNamesMapByConvention()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<SimpleSource, SimpleDest>()).CreateMapper();
        var dest = mapper.Map<SimpleDest>(new SimpleSource { Id = 42, Name = "Ada" });
        Assert.Equal(42, dest.Id);
        Assert.Equal("Ada", dest.Name);
    }

    public class LowerCaseSource
    {
        public int id { get; set; }
        public string? name { get; set; }
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<LowerCaseSource, SimpleDest>()).CreateMapper();
        var dest = mapper.Map<SimpleDest>(new LowerCaseSource { id = 5, name = "Bo" });
        Assert.Equal(5, dest.Id);
        Assert.Equal("Bo", dest.Name);
    }

    public class FieldSource
    {
        public int Id;
        public string? Name;
    }

    [Fact]
    public void PublicFieldsMapToProperties()
    {
        var mapper = new MapperConfiguration(cfg => cfg.CreateMap<FieldSource, SimpleDest>()).CreateMapper();
        var dest = mapper.Map<SimpleDest>(new FieldSource { Id = 9, Name = "Cy" });
        Assert.Equal(9, dest.Id);
        Assert.Equal("Cy", dest.Name);
    }

    public class SnakeSource
    {
        public string? first_name { get; set; }
        public string? last_name { get; set; }
    }

    public class PascalDest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    [Fact]
    public void SnakeCaseSourceMapsToPascalDestWithConvention()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.SourceNamingConvention = LowerUnderscoreNamingConvention.Instance;
            cfg.CreateMap<SnakeSource, PascalDest>();
        }).CreateMapper();
        var dest = mapper.Map<PascalDest>(new SnakeSource { first_name = "Ada", last_name = "Lovelace" });
        Assert.Equal("Ada", dest.FirstName);
        Assert.Equal("Lovelace", dest.LastName);
    }

    public class SnakeDest
    {
        public string? first_name { get; set; }
        public string? last_name { get; set; }
    }

    [Fact]
    public void PascalSourceMapsToSnakeDest()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.DestinationNamingConvention = LowerUnderscoreNamingConvention.Instance;
            cfg.CreateMap<PascalDest, SnakeDest>();
        }).CreateMapper();
        var dest = mapper.Map<SnakeDest>(new PascalDest { FirstName = "Grace", LastName = "Hopper" });
        Assert.Equal("Grace", dest.first_name);
        Assert.Equal("Hopper", dest.last_name);
    }

    public class PrefixedSource
    {
        public string? SrcName { get; set; }
        public int SrcAge { get; set; }
    }

    public class PersonDest
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public void RecognizedSourcePrefixesAreStripped()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.RecognizePrefixes("Src");
            cfg.CreateMap<PrefixedSource, PersonDest>();
        }).CreateMapper();
        var dest = mapper.Map<PersonDest>(new PrefixedSource { SrcName = "Dee", SrcAge = 31 });
        Assert.Equal("Dee", dest.Name);
        Assert.Equal(31, dest.Age);
    }

    public class PostfixedSource
    {
        public string? TitleText { get; set; }
    }

    public class TitleDest
    {
        public string? Title { get; set; }
    }

    [Fact]
    public void RecognizedSourcePostfixesAreStripped()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.RecognizePostfixes("Text");
            cfg.CreateMap<PostfixedSource, TitleDest>();
        }).CreateMapper();
        var dest = mapper.Map<TitleDest>(new PostfixedSource { TitleText = "Hello" });
        Assert.Equal("Hello", dest.Title);
    }

    public class PrefixedDest
    {
        public string? DtoName { get; set; }
    }

    [Fact]
    public void RecognizedDestinationPrefixesAreStripped()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.RecognizeDestinationPrefixes("Dto");
            cfg.CreateMap<SimpleSource, PrefixedDest>();
        }).CreateMapper();
        var dest = mapper.Map<PrefixedDest>(new SimpleSource { Name = "Eli" });
        Assert.Equal("Eli", dest.DtoName);
    }

    public class ExactSource
    {
        public string? NAME { get; set; }
    }

    [Fact]
    public void ExactMatchConventionStillMatchesCaseInsensitively()
    {
        // The exact-match convention disables word splitting; direct member lookup
        // itself is case-insensitive, so NAME still finds Name.
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.SourceNamingConvention = ExactMatchNamingConvention.Instance;
            cfg.CreateMap<ExactSource, SimpleDest>();
        }).CreateMapper();
        var dest = mapper.Map<SimpleDest>(new ExactSource { NAME = "Fay" });
        Assert.Equal("Fay", dest.Name);
    }
}
