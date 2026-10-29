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
    public NonGenericMappingExpression BeforeMap(Action<object, object> action)
    {
        _typeMap.BeforeMapActions.Add((s, d, ctx) => action(s, d));
        return this;
    }
    public NonGenericMappingExpression AfterMap(Action<object, object> action)
    {
        _typeMap.AfterMapActions.Add((s, d, ctx) => action(s, d));
        return this;
    }
    public NonGenericMappingExpression AddValueTransformer<TMember>(Func<TMember, TMember> transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        _typeMap.ValueTransformers.Add(new ValueTransformerRegistration(typeof(TMember), v => transformer(v is TMember typed ? typed : default!)));
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
            Profile profile;
            try { profile = (Profile)Activator.CreateInstance(type)!; }
            catch
            {
                // No parameterless constructor: route through the service
                // constructor (which falls back to default construction and
                // raises the clear configuration error when that fails).
                profile = (Profile)Configuration.ServiceConstructor(type);
            }
            AddProfile(profile);
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

    public void AddValueTransformer<TMember>(Func<TMember, TMember> transformer)
    {
        GuardNotSealed();
        ArgumentNullException.ThrowIfNull(transformer);
        Configuration.GlobalValueTransformers.Add(
            new ValueTransformerRegistration(typeof(TMember), v => transformer(v is TMember typed ? typed : default!)));
    }

    /// <summary>Registers a feature. Overloads register global, mapping, and
    /// runtime features in their respective registries (see
    /// <see cref="MapperConfiguration.GlobalFeatures"/>,
    /// <see cref="MapperConfiguration.MappingFeatures"/>, and
    /// <see cref="MapperConfiguration.RuntimeFeatures"/>).</summary>
    public void AddFeature(Features.IGlobalFeature feature)
    {
        GuardNotSealed();
        Configuration.GlobalFeatures.Add(feature);
    }
    public void AddFeature(Features.IMappingFeature feature)
    {
        GuardNotSealed();
        Configuration.MappingFeatures.Add(feature);
    }
    public void AddFeature(Features.IRuntimeFeature feature)
    {
        GuardNotSealed();
        Configuration.RuntimeFeatures.Add(feature);
    }
    /// <summary>Registers a feature in every registry whose interface it
    /// implements, so one object can be both a mapping feature and a runtime
    /// feature. A feature implementing none of the three interfaces is rejected.</summary>
    public void AddFeature<TFeature>(TFeature feature) where TFeature : class
    {
        GuardNotSealed();
        bool any = false;
        if (feature is Features.IGlobalFeature global) { Configuration.GlobalFeatures.Add(global); any = true; }
        if (feature is Features.IMappingFeature mapping) { Configuration.MappingFeatures.Add(mapping); any = true; }
        if (feature is Features.IRuntimeFeature runtime) { Configuration.RuntimeFeatures.Add(runtime); any = true; }
        if (!any)
            throw new AutoMapperConfigurationException(
                $"Type {typeof(TFeature).FullName} is not a feature: it must implement IGlobalFeature, IMappingFeature, or IRuntimeFeature.");
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
        // Stage 3: global features run once, after user configuration and
        // before sealing, from a snapshot (features they register are not
        // themselves run in this build). Mapping/runtime features are not
        // invoked here; they are consulted at map time.
        foreach (var feature in GlobalFeatures.All)
            feature.Configure(expression);
        Seal();
    }

    /// <summary>Global features: run once at build (see constructor).</summary>
    public Features.Features<Features.IGlobalFeature> GlobalFeatures { get; } = new();
    /// <summary>Mapping features: run around each configured map operation.</summary>
    public Features.Features<Features.IMappingFeature> MappingFeatures { get; } = new();
    /// <summary>Runtime features: reachable from resolvers/hooks via ResolutionContext.</summary>
    public Features.Features<Features.IRuntimeFeature> RuntimeFeatures { get; } = new();

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
    /// <summary>Default service construction: default-construct the type.
    /// Only when that is impossible does the clear configuration error
    /// fire, telling the caller to register ConstructServicesUsing. A
    /// registered constructor replaces this default entirely.</summary>
    public Func<Type, object> ServiceConstructor { get; internal set; } = type =>
    {
        try { return Activator.CreateInstance(type)!; }
        catch (Exception ex)
        {
            throw new AutoMapperConfigurationException(
                $"No service constructor is configured, so an instance of {type.FullName} cannot be created (it has no usable parameterless constructor). Call ConstructServicesUsing during configuration to supply one.", ex);
        }
    };
    public List<ValueTransformerRegistration> GlobalValueTransformers { get; } = new();
    IReadOnlyList<ValueTransformerRegistration> IConfigurationProvider.GlobalValueTransformers => GlobalValueTransformers;

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
                TypeConverterType = open.TypeConverterType,
                ConstructUsingFunc = open.ConstructUsingFunc,
                ConstructUsingWithContext = open.ConstructUsingWithContext,
            };
            closed.BeforeMapActions.AddRange(open.BeforeMapActions);
            closed.AfterMapActions.AddRange(open.AfterMapActions);
            closed.BeforeMapActionTypes.AddRange(open.BeforeMapActionTypes);
            closed.AfterMapActionTypes.AddRange(open.AfterMapActionTypes);
            closed.ValueTransformers.AddRange(open.ValueTransformers);
            closed.IgnoredSourceMembers.UnionWith(open.IgnoredSourceMembers);
            closed.UnvalidatedSourceMembers.UnionWith(open.UnvalidatedSourceMembers);
            closed.IncludedDerived.AddRange(open.IncludedDerived);
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
                // STAGE 2 (proxy): same flattened enumeration as TypeMap, so
                // inherited interface members validate against the proxy shape.
                var destProps = Execution.ProxyGenerator.GetDestinationProperties(map.DestinationType)
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
