using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Validation: AssertConfigurationIsValid reports every unmapped member
/// in one exception, honours MemberList, and dry-runs maps.</summary>
public class ValidationTests
{
    public class Source
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Extra { get; set; }
    }

    public class Dest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? UnmappedOne { get; set; }
        public string? UnmappedTwo { get; set; }
    }

    [Fact]
    public void AllUnmappedMembersReportedInOneException()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.UnmappedOne, o => o.Ignore())
        );
        var ex = Assert.Throws<AutoMapperConfigurationException>(() => config.AssertConfigurationIsValid());
        Assert.Contains("UnmappedTwo", ex.Message);
        Assert.DoesNotContain("UnmappedOne", ex.Message);
    }

    [Fact]
    public void MultipleUnmappedMembersAllAppear()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, Dest>());
        var ex = Assert.Throws<AutoMapperConfigurationException>(() => config.AssertConfigurationIsValid());
        Assert.Contains("UnmappedOne", ex.Message);
        Assert.Contains("UnmappedTwo", ex.Message);
    }

    [Fact]
    public void FullyMappedConfigurationValidatesClean()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>()
                .ForMember(d => d.UnmappedOne, o => o.Ignore())
                .ForMember(d => d.UnmappedTwo, o => o.Ignore())
        );
        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void MemberListNoneSkipsUnmappedChecks()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>(MemberList.None)
        );
        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void MemberListSourceReportsUnmappedSourceMembers()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>(MemberList.Source)
        );
        var ex = Assert.Throws<AutoMapperConfigurationException>(() => config.AssertConfigurationIsValid());
        Assert.Contains("Extra", ex.Message);
    }

    [Fact]
    public void ForSourceMemberIgnoreClearsSourceValidation()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>(MemberList.Source)
                .ForSourceMember(s => s.Extra, o => o.Ignore())
                .ForMember(d => d.UnmappedOne, o => o.Ignore())
                .ForMember(d => d.UnmappedTwo, o => o.Ignore())
        );
        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void ForSourceMemberDoNotValidateClearsSourceValidation()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, Dest>(MemberList.Source)
                .ForSourceMember(s => s.Extra, o => o.DoNotValidate())
                .ForMember(d => d.UnmappedOne, o => o.Ignore())
                .ForMember(d => d.UnmappedTwo, o => o.Ignore())
        );
        config.AssertConfigurationIsValid();
    }

    public interface IShaped
    {
        string? Name { get; set; }
    }

    [Fact]
    public void InterfaceDestinationFailsValidationDryRun()
    {
        var config = new MapperConfiguration(cfg => cfg.CreateMap<Source, IShaped>());
        var ex = Assert.Throws<AutoMapperConfigurationException>(() => config.AssertConfigurationIsValid());
        Assert.Contains("Dry run", ex.Message);
    }

    [Fact]
    public void CtorOnlyMembersDoNotCountAsUnmapped()
    {
        // Identifier has no source member of its own name; it is supplied only
        // through the constructor parameter, so validation must not flag it.
        var config = new MapperConfiguration(cfg =>
            cfg.CreateMap<Source, CtorDest>()
                .ForCtorParam("identifier", o => o.MapFrom(s => s.Id))
        );
        config.AssertConfigurationIsValid();
    }

    public class CtorDest
    {
        public CtorDest(int identifier, string name)
        {
            Identifier = identifier;
            Name = name;
        }

        public int Identifier { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
