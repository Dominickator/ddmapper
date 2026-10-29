using System.Linq.Expressions;

namespace AutoMapper;

/// <summary>Per-operation hooks and items supplied at Map time.</summary>
public interface IMappingOperationOptions
{
    /// <summary>Arbitrary values available to resolvers and hooks during this operation.</summary>
    IDictionary<string, object?> Items { get; }
    /// <summary>Hooks run before the destination is populated.</summary>
    IList<Action<object, object>> BeforeMapActions { get; }
    /// <summary>Hooks run after the destination is populated.</summary>
    IList<Action<object, object>> AfterMapActions { get; }
}

/// <summary>Strongly typed per-operation options.</summary>
public interface IMappingOperationOptions<TSource, TDestination> : IMappingOperationOptions
{
    IList<Action<TSource, TDestination>> TypedBeforeMapActions { get; }
    IList<Action<TSource, TDestination>> TypedAfterMapActions { get; }
}

/// <summary>Converts one whole object to another type.</summary>
public interface ITypeConverter<TSource, TDestination>
{
    TDestination Convert(TSource source, TDestination? destination, ResolutionContext context);
}

/// <summary>Resolves a single destination member value.</summary>
public interface IValueResolver<TSource, TDestination, TMember>
{
    TMember Resolve(TSource source, TDestination destination, TMember destMember, ResolutionContext context);
}

/// <summary>Action run before or after a map.</summary>
public interface IMappingAction<in TSource, in TDestination>
{
    void Process(TSource source, TDestination destination, ResolutionContext context);
}

/// <summary>Read-only view of a finished configuration.</summary>
public interface IConfigurationProvider
{
    /// <summary>All configured type maps, keyed by (source, destination).</summary>
    IReadOnlyDictionary<(Type Source, Type Destination), TypeMap> TypeMaps { get; }
    /// <summary>Creates a mapper bound to this configuration.</summary>
    IMapper CreateMapper();
    /// <summary>Compiles every configured plan now instead of lazily on first use.</summary>
    void CompileMappings();
    /// <summary>Validates every configured map and throws one combined exception on failure.</summary>
    void AssertConfigurationIsValid();
    /// <summary>Finds a map for a pair, closing an open-generic map when one applies.</summary>
    TypeMap? FindTypeMap(Type source, Type destination);
    /// <summary>Function used to construct service instances (resolvers, converters) on demand.</summary>
    Func<Type, object> ServiceConstructor { get; }
    /// <summary>When false, null source members are skipped instead of assigned.</summary>
    bool AllowNullDestinationValues { get; }
    /// <summary>When false, null source collections map to empty destination collections.</summary>
    bool AllowNullCollections { get; }
    /// <summary>Maximum nesting depth for a single map; 0 means unlimited.</summary>
    int MaxDepthDefault { get; }
}

/// <summary>Fluent surface used to configure a single source/destination pair.</summary>
public interface IMappingExpression<TSource, TDestination>
{
    IMappingExpression<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> memberOptions);
    IMappingExpression<TSource, TDestination> ForPath<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> memberOptions);
    IMappingExpression<TSource, TDestination> ForSourceMember<TMember>(
        Expression<Func<TSource, TMember>> sourceMember,
        Action<ISourceMemberConfigurationExpression> memberOptions);
    IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter);
    IMappingExpression<TSource, TDestination> ConvertUsing(ITypeConverter<TSource, TDestination> converter);
    IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> constructor);
    IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, ResolutionContext, TDestination> constructor);
    IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action);
    IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination, ResolutionContext> action);
    IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action);
    IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination, ResolutionContext> action);
    IMappingExpression<TSource, TDestination> IncludeBase<TBaseSource, TBaseDestination>();
    IMappingExpression<TSource, TDestination> Include<TDerivedSource, TDerivedDestination>();
    IMappingExpression<TSource, TDestination> MaxDepth(int depth);
    IMappingExpression<TSource, TDestination> PreserveReferences();
    IMappingExpression<TDestination, TSource> ReverseMap();
    IMappingExpression<TSource, TDestination> ForCtorParam(string paramName, Action<ICtorParamConfigurationExpression<TSource>> options);
    IMappingExpression<TSource, TDestination> DisableCtorValidation();
}

/// <summary>Per-member configuration surface.</summary>
public interface IMemberConfigurationExpression<TSource, TDestination, TMember>
{
    void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember);
    void MapFrom(Expression<Func<TSource, TDestination, TMember>> sourceMember);
    void MapFrom<TResolver>(TResolver resolver) where TResolver : IValueResolver<TSource, TDestination, TMember>;
    void Ignore();
    void Condition(Func<TSource, bool> condition);
    void Condition(Func<TSource, TDestination, bool> condition);
    void Condition(Func<TSource, TDestination, TMember, bool> condition);
    void PreCondition(Func<TSource, bool> condition);
    void NullSubstitute(TMember? substitute);
    void UseDestinationValue();
    void ConvertUsing(ITypeConverter<object, TMember> converter);
    void MapAtRuntime();
}

/// <summary>Configuration for one constructor parameter.</summary>
public interface ICtorParamConfigurationExpression<TSource>
{
    void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember);
}

/// <summary>Per-source-member configuration surface (ignore / do-not-validate).</summary>
public interface ISourceMemberConfigurationExpression
{
    void Ignore();
    void DoNotValidate();
}

/// <summary>Root configuration surface: maps, profiles, and global options.</summary>
public interface IMapperConfigurationExpression
{
    IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>();
    IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(MemberList memberList);
    object CreateMap(Type sourceType, Type destinationType);
    object CreateMap(Type sourceType, Type destinationType, MemberList memberList);
    void AddProfile(Profile profile);
    void AddProfile<TProfile>() where TProfile : Profile, new();
    void AddProfiles(params Type[] profileTypes);
    void AddProfiles(params System.Reflection.Assembly[] assemblies);
    void ConstructServicesUsing(Func<Type, object> constructor);
    void RecognizePrefixes(params string[] prefixes);
    void RecognizePostfixes(params string[] postfixes);
    void RecognizeDestinationPrefixes(params string[] prefixes);
    void RecognizeDestinationPostfixes(params string[] postfixes);
    INamingConvention SourceNamingConvention { get; set; }
    INamingConvention DestinationNamingConvention { get; set; }
    bool AllowNullDestinationValues { get; set; }
    bool AllowNullCollections { get; set; }
}

/// <summary>Mapper entry point.</summary>
public interface IMapper
{
    TDestination Map<TDestination>(object source);
    TDestination Map<TDestination>(object source, Action<IMappingOperationOptions> opts);
    TDestination Map<TSource, TDestination>(TSource source);
    TDestination Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>> opts);
    TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
    TDestination Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>> opts);
    object Map(object source, Type sourceType, Type destinationType);
    object Map(object source, object destination, Type sourceType, Type destinationType);
    IConfigurationProvider ConfigurationProvider { get; }
}
