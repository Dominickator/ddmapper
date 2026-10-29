using System.Reflection;

namespace AutoMapper;

/// <summary>Non-generic map handle returned by CreateMap(Type, Type). Supports whole-type
/// conversion and construction; member-level configuration uses the generic overloads.</summary>
public sealed class NonGenericMappingExpression
{
    private readonly TypeMap _typeMap;
    private readonly MapperConfigurationExpression _configuration;
    internal NonGenericMappingExpression(TypeMap typeMap, MapperConfigurationExpression configuration)
    {
        _typeMap = typeMap;
        _configuration = configuration;
    }
    public NonGenericMappingExpression ConvertUsing(Func<object, object> converter)
    {
        _typeMap.TypeConverterFunc = converter;
        return this;
    }
    public NonGenericMappingExpression ConstructUsing(Func<object, object> constructor)
    {
        _typeMap.ConstructUsingFunc = constructor;
        return this;
    }
    public NonGenericMappingExpression PreserveReferences()
    {
        _typeMap.PreserveReferences = true;
        return this;
    }
    public NonGenericMappingExpression MaxDepth(int depth)
    {
        _typeMap.MaxDepth = depth;
        return this;
    }
}

/// <summary>Mutable configuration surface; becomes sealed once a mapper is created or plans are compiled.</summary>
public sealed class MapperConfigurationExpression : IMapperConfigurationExpression
{
    internal MapperConfigurationExpression(MapperConfiguration configuration) { Configuration = configuration; }
    internal MapperConfiguration Configuration { get; }

    public INamingConvention SourceNamingConvention
    {
        get => Configuration.SourceNamingConvention;
        set => Configuration.SourceNamingConvention = value;
    }
    public INamingConvention DestinationNamingConvention
    {
        get => Configuration.DestinationNamingConvention;
        set => Configuration.DestinationNamingConvention = value;
    }
    public bool AllowNullDestinationValues
    {
        get => Configuration.AllowNullDestinationValues;
        set => Configuration.AllowNullDestinationValues = value;
    }
    public bool AllowNullCollections
    {
        get => Configuration.AllowNullCollections;
        set => Configuration.AllowNullCollections = value;
    }

    internal void GuardNotSealed()
    {
        if (Configuration.IsSealed)
            throw new AutoMapperConfigurationException(
                "This configuration has already been built and can no longer accept new maps. Configure all maps before creating a mapper.");
    }

    public IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>() =>
        CreateMap<TSource, TDestination>(MemberList.Destination);

    public IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(MemberList memberList)
    {
        GuardNotSealed();
        var typeMap = Configuration.GetOrCreateTypeMap(typeof(TSource), typeof(TDestination), memberList);
        return new MappingExpression<TSource, TDestination>(typeMap, this);
    }

    public object CreateMap(Type sourceType, Type destinationType) =>
        CreateMap(sourceType, destinationType, MemberList.Destination);

    public object CreateMap(Type sourceType, Type destinationType, MemberList memberList)
    {
        GuardNotSealed();
        var typeMap = Configuration.GetOrCreateTypeMap(sourceType, destinationType, memberList);
        return new NonGenericMappingExpression(typeMap, this);
    }

    public void AddProfile(Profile profile)
    {
        GuardNotSealed();
        profile.Attach(this);
    }

    public void AddProfile<TProfile>() where TProfile : Profile, new() => AddProfile(new TProfile());

    public void AddProfiles(params Type[] profileTypes)
    {
        foreach (var type in profileTypes)
        {
            if (!typeof(Profile).IsAssignableFrom(type))
                throw new AutoMapperConfigurationException($"Type {type.FullName} is not a Profile and cannot be added as one.");
            AddProfile((Profile)Activator.CreateInstance(type)!);
        }
    }

    public void AddProfiles(params Assembly[] assemblies)
    {
        var profiles = assemblies
            .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; } })
            .Where(t => t != null && typeof(Profile).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();
        AddProfiles(profiles.Where(t => t != null).Select(t => t!).ToArray());
    }

    public void ConstructServicesUsing(Func<Type, object> constructor)
    {
        GuardNotSealed();
        Configuration.ServiceConstructor = constructor;
    }

    public void RecognizePrefixes(params string[] prefixes) => Configuration.SourcePrefixes.AddRange(prefixes);
    public void RecognizePostfixes(params string[] postfixes) => Configuration.SourcePostfixes.AddRange(postfixes);
    public void RecognizeDestinationPrefixes(params string[] prefixes) => Configuration.DestinationPrefixes.AddRange(prefixes);
    public void RecognizeDestinationPostfixes(params string[] postfixes) => Configuration.DestinationPostfixes.AddRange(postfixes);

    internal TypeMap? FindTypeMapInternal(Type source, Type destination) => Configuration.FindTypeMap(source, destination);
}

/// <summary>Sealed, compiled configuration: owns type maps, plan cache, validation, and mapper creation.</summary>
public sealed class MapperConfiguration : IConfigurationProvider
{
    private readonly Dictionary<(Type, Type), TypeMap> _typeMaps = new();
    private readonly Dictionary<(Type, Type), TypeMap> _closedGenericCache = new();
    private readonly object _lock = new();

    public MapperConfiguration(Action<IMapperConfigurationExpression> configure)
    {
        var expression = new MapperConfigurationExpression(this);
        configure(expression);
        Seal();
    }

    public bool IsSealed { get; private set; }
    internal Dictionary<(Type, Type), MappingPlan> CompiledPlans { get; } = new();
    internal object PlanLock { get; } = new();
    public INamingConvention SourceNamingConvention { get; internal set; } = PascalCaseNamingConvention.Instance;
    public INamingConvention DestinationNamingConvention { get; internal set; } = PascalCaseNamingConvention.Instance;
    public bool AllowNullDestinationValues { get; internal set; } = true;
    public bool AllowNullCollections { get; internal set; } = true;
    public int MaxDepthDefault { get; internal set; } = 0;
    public List<string> SourcePrefixes { get; } = new();
    public List<string> SourcePostfixes { get; } = new();
    public List<string> DestinationPrefixes { get; } = new();
    public List<string> DestinationPostfixes { get; } = new();
    public Func<Type, object> ServiceConstructor { get; internal set; } = type =>
        throw new AutoMapperConfigurationException($"No service constructor is configured, so an instance of {type.FullName} cannot be created. Call ConstructServicesUsing during configuration.");

    public IReadOnlyDictionary<(Type Source, Type Destination), TypeMap> TypeMaps => _typeMaps;

    internal TypeMap GetOrCreateTypeMap(Type source, Type destination, MemberList memberList)
    {
        lock (_lock)
        {
            if (_typeMaps.TryGetValue((source, destination), out var existing)) return existing;
            var map = new TypeMap(source, destination, memberList);
            _typeMaps[(source, destination)] = map;
            return map;
        }
    }

    /// <summary>Exact match, then open-generic closing: a map declared for Source&lt;&gt;/Dest&lt;&gt;
    /// is closed over the requested type arguments and cached.</summary>
    public TypeMap? FindTypeMap(Type source, Type destination)
    {
        if (_typeMaps.TryGetValue((source, destination), out var exact)) return exact;
        if (!source.IsGenericType || !destination.IsGenericType) return null;
        lock (_lock)
        {
            if (_closedGenericCache.TryGetValue((source, destination), out var cached)) return cached;
            var openSource = source.GetGenericTypeDefinition();
            var openDest = destination.GetGenericTypeDefinition();
            if (!_typeMaps.TryGetValue((openSource, openDest), out var open)) return null;
            var closed = new TypeMap(source, destination, open.MemberList)
            {
                PreserveReferences = open.PreserveReferences,
                MaxDepth = open.MaxDepth,
                TypeConverterFunc = open.TypeConverterFunc,
                TypeConverter = open.TypeConverter,
            };
            closed.BeforeMapActions.AddRange(open.BeforeMapActions);
            closed.AfterMapActions.AddRange(open.AfterMapActions);
            // Property maps are re-resolved by name against the closed types at plan time;
            // custom expressions recorded against open parameters are not replayed, so a
            // closed map relies on conventional matching plus hooks/converters.
            _closedGenericCache[(source, destination)] = closed;
            return closed;
        }
    }

    private void Seal()
    {
        IsSealed = true;
        foreach (var map in _typeMaps.Values)
        {
            map.ResolveConventionalMaps(this);
            map.IsSealed = true;
        }
    }

    public IMapper CreateMapper() => new Mapper(this);

    public void CompileMappings()
    {
        foreach (var map in _typeMaps.Values.ToList())
            PlanCache.GetOrBuild(this, map);
    }

    public void AssertConfigurationIsValid()
    {
        var errors = new List<string>();
        foreach (var map in _typeMaps.Values)
        {
            if (map.MemberList == MemberList.Destination)
            {
                var destProps = map.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite && p.GetSetMethod() != null);
                foreach (var prop in destProps)
                {
                    var pm = map.FindPropertyMap(prop.Name);
                    bool mapped = pm != null && (pm.Kind != PropertyMapKind.Auto || pm.SourceMemberPath.Length > 0);
                    bool ignored = pm != null && pm.Ignored;
                    bool ctorOnly = map.ConstructorMap.Parameters.Any(p => string.Equals(p.ParameterName, prop.Name, StringComparison.OrdinalIgnoreCase));
                    if (!mapped && !ignored && !ctorOnly)
                        errors.Add($"Destination member {map.DestinationType.Name}.{prop.Name} has no source member in the map from {map.SourceType.Name} to {map.DestinationType.Name}. Map it explicitly or ignore it.");
                }
            }
            else if (map.MemberList == MemberList.Source)
            {
                foreach (var member in MemberMatcher.ReadableMembers(map.SourceType))
                {
                    if (map.IgnoredSourceMembers.Contains(member.Name) || map.UnvalidatedSourceMembers.Contains(member.Name)) continue;
                    var pm = map.PropertyMaps.FirstOrDefault(p => p.SourceMemberPath.Contains(member.Name, StringComparer.OrdinalIgnoreCase));
                    if (pm == null)
                        errors.Add($"Source member {map.SourceType.Name}.{member.Name} is not mapped in the map to {map.DestinationType.Name}. Map it explicitly or ignore it.");
                }
            }
            // Dry run against default instances where constructible.
            try
            {
                var plan = PlanCache.GetOrBuild(this, map);
                object? sourceInstance = null;
                try { sourceInstance = Activator.CreateInstance(map.SourceType); } catch { /* not constructible: skipped */ }
                if (sourceInstance != null)
                {
                    var mapper = new Mapper(this);
                    var ctx = new ResolutionContext(mapper, this, null, map);
                    plan(sourceInstance, null, ctx);
                }
            }
            catch (Exception ex) when (ex is not AutoMapperConfigurationException)
            {
                errors.Add($"Dry run of the map from {map.SourceType.Name} to {map.DestinationType.Name} failed: {ex.Message}");
            }
        }
        if (errors.Count > 0)
            throw new AutoMapperConfigurationException("Configuration validation failed:\n" + string.Join("\n", errors));
    }
}
