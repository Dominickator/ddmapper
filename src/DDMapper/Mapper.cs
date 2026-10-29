using System.Collections;

namespace AutoMapper;

/// <summary>Runtime mapper: resolves one plan per type pair, then invokes the compiled delegate.</summary>
public sealed class Mapper : IMapper
{
    private readonly MapperConfiguration _configuration;

    public Mapper(IConfigurationProvider configurationProvider)
    {
        _configuration = configurationProvider as MapperConfiguration
            ?? throw new AutoMapperConfigurationException("The mapper requires a DDMapper MapperConfiguration.");
    }

    public IConfigurationProvider ConfigurationProvider => _configuration;

    public TDestination Map<TDestination>(object source) => Map<TDestination>(source, (Action<IMappingOperationOptions>?)null);
    public TDestination Map<TDestination>(object source, Action<IMappingOperationOptions>? opts)
    {
        var options = new MappingOperationOptions();
        opts?.Invoke(options);
        var result = MapCore(source, source?.GetType() ?? typeof(object), typeof(TDestination), null, options);
        return (TDestination)result!;
    }
    public TDestination Map<TSource, TDestination>(TSource source)
    {
        var result = MapCore(source, typeof(TSource), typeof(TDestination), null, null);
        return (TDestination)result!;
    }
    public TDestination Map<TSource, TDestination>(TSource source, Action<IMappingOperationOptions<TSource, TDestination>>? opts)
    {
        var options = new MappingOperationOptions<TSource, TDestination>();
        opts?.Invoke(options);
        var result = MapCore(source, typeof(TSource), typeof(TDestination), null, options);
        return (TDestination)result!;
    }
    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination) =>
        Map(source, destination, (Action<IMappingOperationOptions<TSource, TDestination>>?)null);
    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination, Action<IMappingOperationOptions<TSource, TDestination>>? opts)
    {
        var options = new MappingOperationOptions<TSource, TDestination>();
        opts?.Invoke(options);
        var result = MapCore(source, typeof(TSource), typeof(TDestination), destination, options);
        return (TDestination)result!;
    }
    public object Map(object source, Type sourceType, Type destinationType) =>
        MapCore(source, sourceType, destinationType, null, null)!;
    public object Map(object source, object destination, Type sourceType, Type destinationType) =>
        MapCore(source, sourceType, destinationType, destination, null)!;

    private object? MapCore(object? source, Type declaredSourceType, Type destType, object? destination, IMappingOperationOptions? options)
    {
        if (source == null) return destType.IsValueType && Nullable.GetUnderlyingType(destType) == null
            ? Activator.CreateInstance(destType) : null;
        var runtimeSourceType = source.GetType();
        var ctx = new ResolutionContext(this, _configuration, options, null);
        return MapInternal(source, runtimeSourceType, destType, destination, ctx, declaredSourceType);
    }

    internal object? MapInternal(object source, Type sourceType, Type destType, object? destination, ResolutionContext ctx, Type? declaredSourceType = null)
    {
        if (source == null) return null;

        // Derived-type dispatch: a map registered for the runtime type wins over the declared type.
        var typeMap = _configuration.FindTypeMap(sourceType, destType)
            ?? (declaredSourceType != null && declaredSourceType != sourceType ? _configuration.FindTypeMap(declaredSourceType, destType) : null);

        // Included derived maps: if the base map lists this runtime pair, use that map instead.
        if (typeMap != null)
        {
            var derived = typeMap.IncludedDerived.FirstOrDefault(p => p.Source == sourceType && p.Destination == destType);
            if (derived != default)
            {
                var derivedMap = _configuration.FindTypeMap(derived.Source, derived.Destination);
                if (derivedMap != null) typeMap = derivedMap;
            }
            else if (typeMap.IncludedDerived.Count > 0)
            {
                var match = typeMap.IncludedDerived.FirstOrDefault(p => p.Source.IsAssignableFrom(sourceType) && p.Destination == destType);
                if (match != default)
                {
                    var derivedMap = _configuration.FindTypeMap(match.Source, match.Destination);
                    if (derivedMap != null && derivedMap != typeMap) typeMap = derivedMap;
                }
            }
        }

        if (typeMap != null)
        {
            // PreserveReferences: return the already-built destination for a repeated source.
            if (typeMap.PreserveReferences && destination == null && ctx.ReferenceCache.TryGetValue(source, out var cached))
                return cached;
            // Cycle guard: a source already being mapped higher on the stack returns its
            // preserved destination, or the default when references are not preserved.
            if (ctx.ActiveSources.Contains(source))
            {
                if (typeMap.PreserveReferences && ctx.ReferenceCache.TryGetValue(source, out var inFlight)) return inFlight;
                return destType.IsValueType ? Activator.CreateInstance(destType) : null;
            }

            int maxDepth = typeMap.MaxDepth ?? _configuration.MaxDepthDefault;
            if (maxDepth > 0 && ctx.Depth >= maxDepth)
                return destType.IsValueType ? Activator.CreateInstance(destType) : null;

            var plan = PlanCache.GetOrBuild(_configuration, typeMap);
            ctx.ActiveSources.Add(source);
            ctx.Depth++;
            try
            {
                var previousMap = ctx.TypeMap;
                ctx.TypeMap = typeMap;
                try { return plan(source, destination, ctx); }
                finally { ctx.TypeMap = previousMap; }
            }
            finally
            {
                ctx.Depth--;
                ctx.ActiveSources.Remove(source);
            }
        }

        // Dictionary-to-dictionary mapping (key/value mapping, before the generic fallback chain).
        if (MapperRuntime.IsDictionaryType(sourceType) && MapperRuntime.IsDictionaryType(destType))
        {
            var srcArgs = MapperRuntime.DictionaryArgs(sourceType);
            var dstArgs = MapperRuntime.DictionaryArgs(destType);
            if (srcArgs != null && dstArgs != null)
            {
                var resultDict = CreateDictionary(destType, dstArgs.Value.Key, dstArgs.Value.Value);
                foreach (DictionaryEntry entry in (IDictionary)source)
                {
                    var key = entry.Key == null ? null : MapInternal(entry.Key, entry.Key.GetType(), dstArgs.Value.Key, null, ctx);
                    var value = entry.Value == null ? null : MapInternal(entry.Value, entry.Value.GetType(), dstArgs.Value.Value, null, ctx);
                    ((IDictionary)resultDict)[key!] = value;
                }
                return resultDict;
            }
            if (destType.IsAssignableFrom(sourceType)) return source;
        }

        if (FallbackChain.TryMap(this, ctx, source, sourceType, destType, out var fallbackResult))
            return fallbackResult;

        throw new AutoMapperMappingException(
            $"No map is configured from {sourceType.Name} to {destType.Name}, and no built-in conversion handles this pair. Add a CreateMap<{sourceType.Name}, {destType.Name}> entry, or map through an intermediate type.");
    }

    private static object CreateDictionary(Type destType, Type keyType, Type valueType)
    {
        if (!destType.IsAbstract && !destType.IsInterface)
        {
            try { return Activator.CreateInstance(destType)!; } catch { /* fall through */ }
        }
        return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;
    }
}
