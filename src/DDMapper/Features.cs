namespace AutoMapper.Features;

/// <summary>A feature that runs once when a configuration builds, before the
/// configuration is sealed. It receives the configuration expression, so it
/// may register maps, profiles, or further features. Features registered by
/// a global feature itself are not run in the same build (the build runs a
/// snapshot taken before any global feature executes); they take effect on
/// the next configuration built from the same expression, if any.
/// Runs exactly once per MapperConfiguration, however many mappers are
/// created from it or however often mappings are compiled.</summary>
public interface IGlobalFeature
{
    void Configure(IMapperConfigurationExpression configuration);
}

/// <summary>A feature that runs around each configured map operation: its
/// <see cref="BeforeMap"/> runs after the destination is constructed and
/// before any member is assigned, and its <see cref="AfterMap"/> runs after
/// all members are assigned. Mapping features run outside the map's own
/// BeforeMap/AfterMap hooks. They fire for every invocation of a configured
/// type map's plan, including nested maps reached while mapping a member.</summary>
public interface IMappingFeature
{
    void BeforeMap(object source, object destination, ResolutionContext context);
    void AfterMap(object source, object destination, ResolutionContext context);
}

/// <summary>A marker for features that carry runtime state or services.
/// Runtime features are not invoked automatically; they are reachable from
/// resolvers, converters, and hooks through
/// <see cref="ResolutionContext.Features"/> (and
/// <see cref="ResolutionContext.RuntimeFeatures"/>), so a resolver can read
/// feature state that was registered at configuration time.</summary>
public interface IRuntimeFeature
{
}

/// <summary>An ordered, type-keyed registry of features of one kind.
/// Registration happens at configuration time (before sealing); reads are
/// safe during mapping. <see cref="Get{TFeature}"/> returns the first
/// registered feature of the requested type, or null when none is registered.</summary>
public sealed class Features<T> : IEnumerable<T>
{
    private readonly List<T> _features = new();
    private readonly object _lock = new();

    public int Count { get { lock (_lock) { return _features.Count; } } }

    public void Add(T feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        lock (_lock) { _features.Add(feature); }
    }

    public TFeature? Get<TFeature>() where TFeature : class, T
    {
        lock (_lock) { return _features.OfType<TFeature>().FirstOrDefault(); }
    }

    public bool TryGet<TFeature>(out TFeature? feature) where TFeature : class, T
    {
        feature = Get<TFeature>();
        return feature != null;
    }

    public IReadOnlyList<T> All { get { lock (_lock) { return _features.ToList(); } } }

    public IEnumerator<T> GetEnumerator() => All.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
