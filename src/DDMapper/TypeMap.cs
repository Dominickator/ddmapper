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
    /// <summary>Attribute-driven assignment order (see MappingOrderAttribute).
    /// Only meaningful when <see cref="HasMappingOrder"/> is true.</summary>
    public int MappingOrder { get; internal set; }
    public bool HasMappingOrder { get; internal set; }

    /// <summary>Compiled source expression (MapFrom), if any. Null means conventional matching.</summary>
    public LambdaExpression? CustomExpression { get; internal set; }
    /// <summary>Resolver instance (MapFrom with a resolver class), if any.</summary>
    public object? Resolver { get; internal set; }
    /// <summary>Resolver type to instantiate through the service constructor at build time, if any.</summary>
    public Type? ResolverType { get; internal set; }
    /// <summary>Member-value resolver instance (MapFrom with an IMemberValueResolver), if any.</summary>
    public object? MemberResolver { get; internal set; }
    /// <summary>Member-value resolver type to instantiate through the service constructor at build time, if any.</summary>
    public Type? MemberResolverType { get; internal set; }
    /// <summary>Member converter type to instantiate through the service constructor at build time, if any.</summary>
    public Type? ConverterType { get; internal set; }
    /// <summary>Per-member value transformers, in registration order.</summary>
    public List<Func<object?, object?>> ValueTransformers { get; } = new();
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
    public Type? ResolverType { get; internal set; }
    public object? MemberResolver { get; internal set; }
    public Type? MemberResolverType { get; internal set; }
    public object? TypeConverter { get; internal set; }
    public Type? ConverterType { get; internal set; }
    public List<Func<object?, object?>> ValueTransformers { get; } = new();
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
    public Type? TypeConverterType { get; internal set; }
    public List<Type> BeforeMapActionTypes { get; } = new();
    public List<Type> AfterMapActionTypes { get; } = new();
    /// <summary>Map-level value transformers, in registration order.
    /// Each applies only to members whose destination type it covers.</summary>
    public List<ValueTransformerRegistration> ValueTransformers { get; } = new();
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

    /// <summary>Resolves conventional (auto) source chains for every unconfigured destination member,
    /// then applies attribute-driven configuration (Stage 3).
    ///
    /// Precedence, highest first:
    ///   1. Explicit fluent configuration (ForMember / ForPath / ForSourceMember).
    ///      Detected here by any fluent-set field: a custom expression, resolver,
    ///      converter, condition, null substitute, UseDestinationValue, or
    ///      MapAtRuntime flag, or an Ignored/Custom kind. Fluent members are
    ///      left untouched by attributes entirely.
    ///   2. Attributes on the destination member.
    ///   3. Attributes on the (terminal) source member of the resolved chain.
    ///   4. Convention (direct match / flattening).
    /// A destination SourceMember attribute replaces step 4 with its named
    /// chain. Attribute resolver/converter types are instantiated through the
    /// configuration's service constructor at this point, once per build.</summary>
    /// <summary>Instantiates every deferred service type recorded on this
    /// map through the configuration's service constructor. Idempotent:
    /// each type is cleared after it is resolved, so a second call is a
    /// no-op. Runs before conventional matching so fluent type-based
    /// members count as fluent-configured below.</summary>
    internal void ResolveDeferredServices(MapperConfiguration configuration)
    {
        if (TypeConverter == null && TypeConverterType != null)
        {
            TypeConverter = configuration.ServiceConstructor(TypeConverterType);
            TypeConverterType = null;
        }
        foreach (var type in BeforeMapActionTypes.ToList())
        {
            var action = (object)configuration.ServiceConstructor(type);
            var method = type.GetMethod("Process")!;
            BeforeMapActions.Add((s, d, ctx) => method.Invoke(action, new[] { s, d, ctx }));
        }
        BeforeMapActionTypes.Clear();
        foreach (var type in AfterMapActionTypes.ToList())
        {
            var action = (object)configuration.ServiceConstructor(type);
            var method = type.GetMethod("Process")!;
            AfterMapActions.Add((s, d, ctx) => method.Invoke(action, new[] { s, d, ctx }));
        }
        AfterMapActionTypes.Clear();
        foreach (var pm in PropertyMaps)
        {
            if (pm.Resolver == null && pm.ResolverType != null)
            {
                pm.Resolver = configuration.ServiceConstructor(pm.ResolverType);
                pm.ResolverType = null;
            }
            if (pm.MemberResolver == null && pm.MemberResolverType != null)
            {
                pm.MemberResolver = configuration.ServiceConstructor(pm.MemberResolverType);
                pm.MemberResolverType = null;
            }
            if (pm.TypeConverter == null && pm.ConverterType != null)
            {
                pm.TypeConverter = configuration.ServiceConstructor(pm.ConverterType);
                pm.ConverterType = null;
            }
        }
        foreach (var path in PathMaps)
        {
            if (path.Resolver == null && path.ResolverType != null)
            {
                path.Resolver = configuration.ServiceConstructor(path.ResolverType);
                path.ResolverType = null;
            }
            if (path.MemberResolver == null && path.MemberResolverType != null)
            {
                path.MemberResolver = configuration.ServiceConstructor(path.MemberResolverType);
                path.MemberResolverType = null;
            }
            if (path.TypeConverter == null && path.ConverterType != null)
            {
                path.TypeConverter = configuration.ServiceConstructor(path.ConverterType);
                path.ConverterType = null;
            }
        }
    }

    internal void ResolveConventionalMaps(MapperConfiguration configuration)
    {
        ResolveDeferredServices(configuration);
        // STAGE 2 (proxy): flatten inherited interface properties so a proxy
        // destination mapping covers base-interface members too.
        var destProps = Execution.ProxyGenerator.GetDestinationProperties(DestinationType)
            .Where(p => p.CanWrite && p.GetSetMethod() != null).ToList();
        foreach (var prop in destProps)
        {
            var existing = PropertyMaps.FirstOrDefault(p => p.DestinationProperty == prop);
            // Custom maps skip re-resolution, except member-resolver
            // maps, which still need their conventional source chain
            // (the resolver receives that chain's value as its source
            // member argument).
            if (existing != null && (existing.Kind == PropertyMapKind.Ignored || existing.Kind == PropertyMapKind.Path))
                continue;
            if (existing != null && existing.Kind == PropertyMapKind.Custom
                && existing.MemberResolver == null && existing.MemberResolverType == null)
                continue;
            var map = existing ?? GetOrCreatePropertyMap(prop);
            if (map.Kind == PropertyMapKind.Ignored) continue;
            if (map.Kind == PropertyMapKind.Custom
                && map.MemberResolver == null && map.MemberResolverType == null) continue;
            if (IsFluentConfigured(map))
            {
                // Fluent wins over attributes, but a fluent member that only
                // sets a condition, substitute, converter, or flag still needs
                // its conventional source chain (the old behavior). Fill it
                // when empty and skip attribute application entirely.
                if (map.SourceMemberPath.Length == 0 && map.CustomExpression == null && map.Resolver == null)
                {
                    var fluentChain = MemberMatcher.FindSourceChain(SourceType, prop.Name, configuration);
                    if (fluentChain != null)
                    {
                        map.SourceMemberPath = fluentChain.Select(m => m.Name).ToArray();
                        map.SourceMembers = fluentChain;
                        map.Kind = fluentChain.Length > 1 ? PropertyMapKind.Flattened : PropertyMapKind.Auto;
                    }
                }
                continue;
            }

            // Destination attributes first: Ignore short-circuits everything else.
            if (Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.IgnoreAttribute>(prop) != null)
            {
                map.Kind = PropertyMapKind.Ignored;
                continue;
            }

            // Destination SourceMember redirect replaces conventional matching.
            var redirect = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.SourceMemberAttribute>(prop);
            MemberInfo[]? chain = null;
            if (redirect != null)
            {
                chain = ResolveNamedChain(SourceType, redirect.MemberName, configuration);
                if (chain == null)
                    throw new AutoMapperConfigurationException(
                        $"The source member '{redirect.MemberName}' named on {DestinationType.Name}.{prop.Name} was not found on {SourceType.Name}. Check the attribute value.");
                map.SourceMemberPath = chain.Select(m => m.Name).ToArray();
                map.SourceMembers = chain;
                map.Kind = chain.Length > 1 ? PropertyMapKind.Flattened : PropertyMapKind.Auto;
            }
            else
            {
                chain = MemberMatcher.FindSourceChain(SourceType, prop.Name, configuration);
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

            ApplyAttributes(map, prop, chain?.Length > 0 ? chain[^1] : null, configuration);
        }

        // MappingOrder: ordered members first (ascending, stable), then the
        // unordered remainder in their original relative order. The plan
        // builder iterates PropertyMaps in list order, so this is what makes
        // attribute order observable at map time.
        if (PropertyMaps.Any(p => p.HasMappingOrder))
        {
            var ordered = PropertyMaps.Where(p => p.HasMappingOrder).OrderBy(p => p.MappingOrder).ToList();
            var unordered = PropertyMaps.Where(p => !p.HasMappingOrder).ToList();
            PropertyMaps.Clear();
            PropertyMaps.AddRange(ordered);
            PropertyMaps.AddRange(unordered);
        }
    }

    /// <summary>True when any fluent option has touched this member. At build
    /// time (before attributes are applied) every field below can only have
    /// come from fluent configuration, so this is a reliable precedence test
    /// without an extra bookkeeping flag.</summary>
    private static bool IsFluentConfigured(PropertyMap map) =>
        map.CustomExpression != null || map.Resolver != null || map.ResolverType != null
        || map.MemberResolver != null || map.MemberResolverType != null
        || map.TypeConverter != null || map.ConverterType != null
        || map.HasNullSubstitute || map.UseDestinationValue || map.MapAtRuntime
        || map.Condition != null || map.PreCondition != null || map.ValueTransformers.Count > 0;

    /// <summary>Applies destination attributes, falling back per-behavior to
    /// attributes on the terminal source member. Destination wins over source
    /// for each behavior independently.</summary>
    private static void ApplyAttributes(PropertyMap map, PropertyInfo destProp, MemberInfo? sourceMember, MapperConfiguration configuration)
    {
        // Source-member Ignore: a source member marked ignore is not mapped.
        // (Destination Ignore was handled by the caller before this point.)
        if (sourceMember != null && Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.IgnoreAttribute>(sourceMember) != null)
        {
            map.Kind = PropertyMapKind.Ignored;
            return;
        }

        var destOrder = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.MappingOrderAttribute>(destProp);
        var srcOrder = sourceMember != null ? Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.MappingOrderAttribute>(sourceMember) : null;
        var order = destOrder ?? srcOrder;
        if (order != null) { map.MappingOrder = order.Order; map.HasMappingOrder = true; }

        var destSub = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.NullSubstituteAttribute>(destProp);
        var srcSub = sourceMember != null ? Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.NullSubstituteAttribute>(sourceMember) : null;
        var sub = destSub ?? srcSub;
        if (sub != null) { map.NullSubstitute = sub.Substitute; map.HasNullSubstitute = true; }

        var destKeep = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.UseDestinationValueAttribute>(destProp);
        var srcKeep = sourceMember != null ? Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.UseDestinationValueAttribute>(sourceMember) : null;
        if (destKeep != null || srcKeep != null) map.UseDestinationValue = true;

        var destRuntime = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.MapAtRuntimeAttribute>(destProp);
        var srcRuntime = sourceMember != null ? Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.MapAtRuntimeAttribute>(sourceMember) : null;
        if (destRuntime != null || srcRuntime != null) map.MapAtRuntime = true;

        var destConv = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.ValueConverterAttribute>(destProp);
        var srcConv = sourceMember != null ? Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.ValueConverterAttribute>(sourceMember) : null;
        var conv = destConv ?? srcConv;
        if (conv != null) map.TypeConverter = configuration.ServiceConstructor(conv.ConverterType);

        var destRes = Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.ValueResolverAttribute>(destProp);
        var srcRes = sourceMember != null ? Configuration.Annotations.AttributeReader.Get<Configuration.Annotations.ValueResolverAttribute>(sourceMember) : null;
        var res = destRes ?? srcRes;
        if (res != null) map.Resolver = configuration.ServiceConstructor(res.ResolverType);
    }

    /// <summary>Resolves a dotted member name ("Customer.Name") against a type,
    /// walking readable members one segment at a time.</summary>
    private static MemberInfo[]? ResolveNamedChain(Type sourceType, string memberName, MapperConfiguration configuration)
    {
        var segments = memberName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        var chain = new List<MemberInfo>();
        var current = sourceType;
        foreach (var segment in segments)
        {
            var member = MemberMatcher.ReadableMembers(current)
                .FirstOrDefault(m => string.Equals(m.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (member == null) return null;
            chain.Add(member);
            current = MemberMatcher.MemberType(member);
        }
        return chain.ToArray();
    }
}
