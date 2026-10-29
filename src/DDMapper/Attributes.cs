using System.Reflection;

namespace AutoMapper.Configuration.Annotations;

/// <summary>Marks a destination member (or its source member) as unmapped.
/// The member is skipped at plan time and counts as ignored for validation.
/// Precedence: explicit fluent configuration beats this attribute; a
/// destination-member attribute beats a source-member attribute.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreAttribute : Attribute
{
}

/// <summary>Redirects a destination member to a named source member instead of
/// the conventional match. <see cref="MemberName"/> may be a single member name
/// or a dotted chain (e.g. "Customer.Name"), which is resolved as a flattened
/// source chain. Applies only on destination members; a matching attribute on
/// a source member has no effect.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class SourceMemberAttribute : Attribute
{
    public SourceMemberAttribute(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
            throw new ArgumentException("A source member name is required.", nameof(memberName));
        MemberName = memberName;
    }
    public string MemberName { get; }
}

/// <summary>Controls the order in which destination members are assigned.
/// Members carrying this attribute are assigned first, in ascending
/// <see cref="Order"/>; members without it keep their original relative order
/// after them. Assignment order is observable when a setter, resolver, or
/// converter has a side effect (e.g. appending to a log).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class MappingOrderAttribute : Attribute
{
    public MappingOrderAttribute(int order) { Order = order; }
    public int Order { get; }
}

/// <summary>Supplies the value used when the resolved source value is null.
/// The substitute is stored as-is and assigned without further mapping, the
/// same as the fluent NullSubstitute option. Attribute arguments must be
/// compile-time constants, so only constant values can be supplied here.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class NullSubstituteAttribute : Attribute
{
    public NullSubstituteAttribute(object? substitute) { Substitute = substitute; }
    public object? Substitute { get; }
}

/// <summary>Keeps the destination member's existing value instead of assigning
/// a mapped source value, the same as the fluent UseDestinationValue option.
/// Applies when mapping onto an existing destination; for a fresh destination
/// the member simply keeps its default value.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class UseDestinationValueAttribute : Attribute
{
}

/// <summary>Resolves a member through a value-converter type instead of the
/// default mapping. The type is instantiated once, at configuration build,
/// through the configuration's service constructor (see
/// <c>ConstructServicesUsing</c>); it must expose a <c>Convert</c> method
/// compatible with the member's source and destination types, the same shape
/// the fluent ConvertUsing option expects.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ValueConverterAttribute : Attribute
{
    public ValueConverterAttribute(Type converterType)
    {
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }
    public Type ConverterType { get; }
}

/// <summary>Resolves a member through a value-resolver type instead of the
/// conventional source chain. The type is instantiated once, at configuration
/// build, through the configuration's service constructor; it must expose a
/// <c>Resolve</c> method compatible with the source, destination, and member
/// types, the same shape the fluent resolver option expects. A resolver
/// attribute also makes the member assignable when no source member matches
/// by name: the resolver, not a source chain, supplies the value.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ValueResolverAttribute : Attribute
{
    public ValueResolverAttribute(Type resolverType)
    {
        ResolverType = resolverType ?? throw new ArgumentNullException(nameof(resolverType));
    }
    public Type ResolverType { get; }
}

/// <summary>Marks a member as mapped at runtime rather than from the
/// plan-time source chain. At map time the source chain is re-resolved by
/// name against the runtime source type, so a derived runtime source whose
/// member hides the declared source member supplies its own value. When no
/// plan-time chain exists, the destination member name is used for the
/// runtime lookup.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class MapAtRuntimeAttribute : Attribute
{
}

/// <summary>Small reflection helper for attribute-driven configuration.
/// Kept here so TypeMap building does not scatter GetCustomAttribute calls.</summary>
internal static class AttributeReader
{
    public static T? Get<T>(MemberInfo member) where T : Attribute =>
        member.GetCustomAttribute<T>(inherit: true);
}
