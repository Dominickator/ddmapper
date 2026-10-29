using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper;

/// <summary>How a single destination member gets its value.</summary>
public enum PropertyMapKind { Auto, Custom, Ignored, Flattened, Path, CtorOnly }

/// <summary>Configuration for one destination member.</summary>
public sealed class PropertyMap
{
    internal PropertyMap(PropertyInfo destinationProperty)
    {
        DestinationProperty = destinationProperty;
        DestinationName = destinationProperty.Name;
        DestinationType = destinationProperty.PropertyType;
    }

    public PropertyInfo DestinationProperty { get; }
    public string DestinationName { get; }
    public Type DestinationType { get; }
    public PropertyMapKind Kind { get; internal set; } = PropertyMapKind.Auto;
    public bool Ignored => Kind == PropertyMapKind.Ignored;
    public bool UseDestinationValue { get; internal set; }
    public bool MapAtRuntime { get; internal set; }

    /// <summary>Compiled source expression (MapFrom), if any. Null means conventional matching.</summary>
    public LambdaExpression? CustomExpression { get; internal set; }
    /// <summary>Resolver instance (MapFrom with a resolver class), if any.</summary>
    public object? Resolver { get; internal set; }
    /// <summary>Source member chain for conventional / flattened / path maps, e.g. ["Customer", "Name"].</summary>
    public string[] SourceMemberPath { get; internal set; } = Array.Empty<string>();
    /// <summary>Source member infos resolved for the chain, when known at plan time.</summary>
    public MemberInfo[] SourceMembers { get; internal set; } = Array.Empty<MemberInfo>();
    internal Func<object, object, object?, ResolutionContext, bool>? Condition { get; set; }
    internal Func<object, bool>? PreCondition { get; set; }
    public object? NullSubstitute { get; internal set; }
    public bool HasNullSubstitute { get; internal set; }
    public object? TypeConverter { get; internal set; }

    internal object? ResolveCustom(object source, object destination, ResolutionContext ctx)
    {
        if (Resolver != null)
        {
            var method = Resolver.GetType().GetMethod("Resolve")!;
            return method.Invoke(Resolver, new[] { source, destination, GetCurrent(destination), ctx });
        }
        if (CustomExpression != null)
        {
            var compiled = CustomExpression.Compile();
            var args = CustomExpression.Parameters.Count == 2
                ? new object?[] { source, destination }
                : new object?[] { source };
            return compiled.DynamicInvoke(args);
        }
        return null;
    }

    private object? GetCurrent(object destination)
    {
        try { return DestinationProperty.GetValue(destination); } catch { return null; }
    }
}

/// <summary>Configuration for one destination path (nested destination member).</summary>
public sealed class PathMap
{
    internal PathMap(string[] destinationPath, Type destinationType)
    {
        DestinationPath = destinationPath;
        DestinationType = destinationType;
    }
    public string[] DestinationPath { get; }
    public Type DestinationType { get; }
    public LambdaExpression? CustomExpression { get; internal set; }
    public object? Resolver { get; internal set; }
    public bool Ignored { get; internal set; }
    internal Func<object, object, object?, ResolutionContext, bool>? Condition { get; set; }
    public object? NullSubstitute { get; internal set; }
    public bool HasNullSubstitute { get; internal set; }
}

/// <summary>Configuration for one constructor parameter.</summary>
public sealed class ConstructorParameterMap
{
    internal ConstructorParameterMap(string name, Type type)
    {
        ParameterName = name;
        ParameterType = type;
    }
    public string ParameterName { get; }
    public Type ParameterType { get; }
    public LambdaExpression? CustomExpression { get; internal set; }
    public string[] SourceMemberPath { get; internal set; } = Array.Empty<string>();
}

/// <summary>Configuration for the destination constructor.</summary>
public sealed class ConstructorMap
{
    public ConstructorInfo? Constructor { get; internal set; }
    public List<ConstructorParameterMap> Parameters { get; } = new();
    public bool DisableValidation { get; internal set; }
}

/// <summary>A configured source/destination pair: property maps, path maps, constructor map, hooks, and flags.</summary>
public sealed class TypeMap
{
    internal TypeMap(Type sourceType, Type destinationType, MemberList memberList)
    {
        SourceType = sourceType;
        DestinationType = destinationType;
        MemberList = memberList;
    }

    public Type SourceType { get; }
    public Type DestinationType { get; }
    public MemberList MemberList { get; }
    public List<PropertyMap> PropertyMaps { get; } = new();
    public List<PathMap> PathMaps { get; } = new();
    public ConstructorMap ConstructorMap { get; } = new();
    public List<Action<object, object, ResolutionContext>> BeforeMapActions { get; } = new();
    public List<Action<object, object, ResolutionContext>> AfterMapActions { get; } = new();
    public Func<object, object>? TypeConverterFunc { get; internal set; }
    public object? TypeConverter { get; internal set; }
    public Func<object, object>? ConstructUsingFunc { get; internal set; }
    public Func<object, ResolutionContext, object>? ConstructUsingWithContext { get; internal set; }
    public int? MaxDepth { get; internal set; }
    public bool PreserveReferences { get; internal set; }
    public HashSet<string> IgnoredSourceMembers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnvalidatedSourceMembers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(Type Source, Type Destination)> IncludedDerived { get; } = new();
    public (Type Source, Type Destination)? IncludedBase { get; internal set; }
    public bool IsSealed { get; internal set; }

    public PropertyMap? FindPropertyMap(string name) =>
        PropertyMaps.FirstOrDefault(p => string.Equals(p.DestinationName, name, StringComparison.OrdinalIgnoreCase));

    internal PropertyMap GetOrCreatePropertyMap(PropertyInfo property)
    {
        var existing = PropertyMaps.FirstOrDefault(p => p.DestinationProperty == property);
        if (existing != null) return existing;
        var map = new PropertyMap(property);
        PropertyMaps.Add(map);
        return map;
    }

    /// <summary>Resolves conventional (auto) source chains for every unconfigured destination member.</summary>
    internal void ResolveConventionalMaps(MapperConfiguration configuration)
    {
        var destProps = DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetSetMethod() != null).ToList();
        foreach (var prop in destProps)
        {
            var existing = PropertyMaps.FirstOrDefault(p => p.DestinationProperty == prop);
            if (existing != null && (existing.Kind == PropertyMapKind.Custom || existing.Kind == PropertyMapKind.Ignored || existing.Kind == PropertyMapKind.Path))
                continue;
            var map = existing ?? GetOrCreatePropertyMap(prop);
            if (map.Kind == PropertyMapKind.Custom || map.Kind == PropertyMapKind.Ignored) continue;
            var chain = MemberMatcher.FindSourceChain(SourceType, prop.Name, configuration);
            if (chain != null)
            {
                map.SourceMemberPath = chain.Select(m => m.Name).ToArray();
                map.SourceMembers = chain;
                map.Kind = chain.Length > 1 ? PropertyMapKind.Flattened : PropertyMapKind.Auto;
            }
            else
            {
                map.SourceMemberPath = Array.Empty<string>();
                map.Kind = PropertyMapKind.Auto;
            }
        }
    }
}
