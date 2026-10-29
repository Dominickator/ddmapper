namespace AutoMapper;

/// <summary>State carried through a single top-level Map call: depth, reference cache, and per-operation items.</summary>
public sealed class ResolutionContext
{
    internal ResolutionContext(IMapper mapper, IConfigurationProvider configuration,
        IMappingOperationOptions? options, TypeMap? typeMap)
    {
        Mapper = mapper;
        Configuration = configuration;
        Options = options;
        TypeMap = typeMap;
    }

    public IMapper Mapper { get; }
    public IConfigurationProvider Configuration { get; }
    public IMappingOperationOptions? Options { get; }
    /// <summary>Runtime features registered on this configuration, reachable
    /// from resolvers, converters, and hooks. Empty when the configuration
    /// is not a DDMapper MapperConfiguration or no runtime features were
    /// registered. This is the Stage 3 accessor for the features system.</summary>
    public Features.Features<Features.IRuntimeFeature> Features =>
        (Configuration as MapperConfiguration)?.RuntimeFeatures ?? EmptyRuntimeFeatures;
    /// <summary>Alias of <see cref="Features"/>.</summary>
    public Features.Features<Features.IRuntimeFeature> RuntimeFeatures => Features;
    /// <summary>Mapping features registered on this configuration.</summary>
    public Features.Features<Features.IMappingFeature> MappingFeatures =>
        (Configuration as MapperConfiguration)?.MappingFeatures ?? EmptyMappingFeatures;
    private static readonly Features.Features<Features.IRuntimeFeature> EmptyRuntimeFeatures = new();
    private static readonly Features.Features<Features.IMappingFeature> EmptyMappingFeatures = new();
    internal TypeMap? TypeMap { get; set; }
    internal int Depth { get; set; }
    internal HashSet<object> ActiveSources { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<object, object> ReferenceCache { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>Per-operation items, supplied through the Map options
    /// (for example <c>opts.Items["key"] = value</c>). Resolvers,
    /// converters, and hooks read them through the context; empty when
    /// no options were supplied.</summary>
    public IDictionary<string, object?> Items => Options?.Items ?? EmptyItems;
    private static readonly Dictionary<string, object?> EmptyItems = new();

    internal ResolutionContext Child(TypeMap? typeMap) =>
        new(Mapper, Configuration, Options, typeMap)
        {
            Depth = Depth,
            // Share caches with the parent so cycles and preserved references span the whole operation.
            // ActiveSources/ReferenceCache are replaced below by sharing via the parent's instances.
        };

    internal ResolutionContext Fork(TypeMap? typeMap)
    {
        var child = new ResolutionContext(Mapper, Configuration, Options, typeMap) { Depth = Depth };
        foreach (var s in ActiveSources) child.ActiveSources.Add(s);
        foreach (var kv in ReferenceCache) child.ReferenceCache[kv.Key] = kv.Value;
        return child;
    }
}

/// <summary>Default per-operation options, created fresh for each Map call.</summary>
public sealed class MappingOperationOptions : IMappingOperationOptions
{
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
    public IList<Action<object, object>> BeforeMapActions { get; } = new List<Action<object, object>>();
    public IList<Action<object, object>> AfterMapActions { get; } = new List<Action<object, object>>();
}

/// <summary>Internal bridge so compiled plans can invoke typed per-operation hooks
/// without knowing the operation's type arguments.</summary>
internal interface ITypedMappingActions
{
    void InvokeTypedBefore(object source, object destination);
    void InvokeTypedAfter(object source, object destination);
}

/// <summary>Strongly typed per-operation options.</summary>
public sealed class MappingOperationOptions<TSource, TDestination> : IMappingOperationOptions<TSource, TDestination>, ITypedMappingActions
{
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
    public IList<Action<object, object>> BeforeMapActions { get; } = new List<Action<object, object>>();
    public IList<Action<object, object>> AfterMapActions { get; } = new List<Action<object, object>>();
    public IList<Action<TSource, TDestination>> TypedBeforeMapActions { get; } = new List<Action<TSource, TDestination>>();
    public IList<Action<TSource, TDestination>> TypedAfterMapActions { get; } = new List<Action<TSource, TDestination>>();

    void ITypedMappingActions.InvokeTypedBefore(object source, object destination)
    {
        foreach (var action in TypedBeforeMapActions) action((TSource)source, (TDestination)destination);
    }

    void ITypedMappingActions.InvokeTypedAfter(object source, object destination)
    {
        foreach (var action in TypedAfterMapActions) action((TSource)source, (TDestination)destination);
    }
}
