using AutoMapper;
using Xunit;

namespace AutoMapper.Tests;

/// <summary>Conversions: nullable unwrapping both ways, enum conversions
/// (string, enum-to-enum, underlying type), and string conversions
/// (ToString fallback, ParseString, IConvertible).</summary>
public class ConversionTests
{
    public class Numbers
    {
        public int? MaybeInt { get; set; }
        public int PlainInt { get; set; }
        public int? TargetNullable { get; set; }
        public int? ToLongNullable { get; set; }
        public string? Text { get; set; }
    }

    public class NumbersDto
    {
        public int Unwrapped { get; set; }
        public int? Wrapped { get; set; }
        public long? LongWrapped { get; set; }
        public int Parsed { get; set; }
        public string? Stringified { get; set; }
    }

    [Fact]
    public void NullableSourceUnwrapsToValueType()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Numbers, NumbersDto>()
                .ForMember(d => d.Unwrapped, o => o.MapFrom(s => s.MaybeInt))
        ).CreateMapper();
        Assert.Equal(5, mapper.Map<NumbersDto>(new Numbers { MaybeInt = 5 }).Unwrapped);
        Assert.Equal(0, mapper.Map<NumbersDto>(new Numbers { MaybeInt = null }).Unwrapped);
    }

    [Fact]
    public void ValueTypeWrapsIntoNullableDestination()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<Numbers, NumbersDto>()
                .ForMember(d => d.Wrapped, o => o.MapFrom(s => s.PlainInt))
                .ForMember(d => d.LongWrapped, o => o.MapFrom(s => s.MaybeInt))
        ).CreateMapper();
        var dest = mapper.Map<NumbersDto>(new Numbers { PlainInt = 7, MaybeInt = 9 });
        Assert.Equal(7, dest.Wrapped);
        Assert.Equal(9L, dest.LongWrapped);
        Assert.Null(mapper.Map<NumbersDto>(new Numbers { MaybeInt = null }).LongWrapped);
    }

    public enum SrcStatus { Active, Paused, Retired }

    public enum DstStatus { Active, Paused, Other }

    public class StatusSource
    {
        public string? Text { get; set; }
        public SrcStatus EnumValue { get; set; }
        public int Number { get; set; }
        public DstStatus Existing { get; set; }
    }

    public class StatusDest
    {
        public DstStatus FromText { get; set; }
        public DstStatus FromEnum { get; set; }
        public int ToUnderlying { get; set; }
        public DstStatus FromNumber { get; set; }
    }

    [Fact]
    public void StringToEnumIsCaseInsensitive()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<StatusSource, StatusDest>()
                .ForMember(d => d.FromText, o => o.MapFrom(s => s.Text))
        ).CreateMapper();
        Assert.Equal(DstStatus.Paused, mapper.Map<StatusDest>(new StatusSource { Text = "paused" }).FromText);
        Assert.Equal(DstStatus.Active, mapper.Map<StatusDest>(new StatusSource { Text = "ACTIVE" }).FromText);
    }

    [Fact]
    public void EnumToEnumMatchesByNameThenFallsBackToValue()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<StatusSource, StatusDest>()
                .ForMember(d => d.FromEnum, o => o.MapFrom(s => s.EnumValue))
        ).CreateMapper();
        Assert.Equal(DstStatus.Paused, mapper.Map<StatusDest>(new StatusSource { EnumValue = SrcStatus.Paused }).FromEnum);
        // "Retired" has no counterpart in DstStatus, so the numeric value carries over.
        Assert.Equal((DstStatus)2, mapper.Map<StatusDest>(new StatusSource { EnumValue = SrcStatus.Retired }).FromEnum);
    }

    [Fact]
    public void EnumToUnderlyingAndNumberToEnum()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<StatusSource, StatusDest>()
                .ForMember(d => d.ToUnderlying, o => o.MapFrom(s => s.EnumValue))
                .ForMember(d => d.FromNumber, o => o.MapFrom(s => s.Number))
        ).CreateMapper();
        var dest = mapper.Map<StatusDest>(new StatusSource { EnumValue = SrcStatus.Paused, Number = 1 });
        Assert.Equal(1, dest.ToUnderlying);
        Assert.Equal(DstStatus.Paused, dest.FromNumber);
    }

    public class TextSource
    {
        public int Count { get; set; }
        public string? NumberText { get; set; }
        public string? GuidText { get; set; }
        public string? DateText { get; set; }
        public double Ratio { get; set; }
    }

    public class TextDest
    {
        public string? CountText { get; set; }
        public int ParsedNumber { get; set; }
        public Guid ParsedGuid { get; set; }
        public DateTime ParsedDate { get; set; }
        public decimal AsDecimal { get; set; }
    }

    [Fact]
    public void ToStringFallbackAndConvertible()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<TextSource, TextDest>()
                .ForMember(d => d.CountText, o => o.MapFrom(s => s.Count))
                .ForMember(d => d.ParsedNumber, o => o.MapFrom(s => s.NumberText))
                .ForMember(d => d.AsDecimal, o => o.MapFrom(s => s.Ratio))
        ).CreateMapper();
        var dest = mapper.Map<TextDest>(new TextSource { Count = 42, NumberText = "123", Ratio = 1.5 });
        Assert.Equal("42", dest.CountText);
        Assert.Equal(123, dest.ParsedNumber);
        Assert.Equal(1.5m, dest.AsDecimal);
    }

    [Fact]
    public void ParseStringHandlesGuidAndDateTime()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<TextSource, TextDest>()
                .ForMember(d => d.ParsedGuid, o => o.MapFrom(s => s.GuidText))
                .ForMember(d => d.ParsedDate, o => o.MapFrom(s => s.DateText))
        ).CreateMapper();
        var guid = Guid.NewGuid();
        var dest = mapper.Map<TextDest>(new TextSource { GuidText = guid.ToString(), DateText = "2026-03-04" });
        Assert.Equal(guid, dest.ParsedGuid);
        Assert.Equal(new DateTime(2026, 3, 4), dest.ParsedDate);
    }

    [Fact]
    public void ByteBackedEnumConvertsToUnderlying()
    {
        var mapper = new MapperConfiguration(cfg =>
            cfg.CreateMap<ByteEnumSource, ByteEnumDest>()
                .ForMember(d => d.LevelValue, o => o.MapFrom(s => s.Level))
        ).CreateMapper();
        var dest = mapper.Map<ByteEnumDest>(new ByteEnumSource { Level = ByteLevel.High });
        Assert.Equal((byte)2, dest.LevelValue);
    }

    public enum ByteLevel : byte { Low, Medium, High }

    public class ByteEnumSource
    {
        public ByteLevel Level { get; set; }
    }

    public class ByteEnumDest
    {
        public byte LevelValue { get; set; }
    }
}
