using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper;

/// <summary>Context handed to projection mappers while a value expression is
/// being built. The builder itself (not the mappers) owns TypeMap lookup,
/// recursion caps, and the final type check.</summary>
public sealed class ProjectionBuildContext
{
    internal ProjectionBuildContext(IConfigurationProvider configuration, IReadOnlyDictionary<string, object?>? parameters)
    {
        Configuration = configuration;
        Parameters = parameters;
    }
    public IConfigurationProvider Configuration { get; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; }
    internal List<(Type Source, Type Destination)> Stack { get; } = new();
    internal Func<Expression, Type, Type, Expression> BuildValue { get; set; } = null!;
}

/// <summary>One step of the projection value-conversion chain. A mapper
/// returns false when the pair is outside its scope so the next mapper
/// in the registry order is tried.</summary>
public interface IProjectionMapper
{
    bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result);
}

/// <summary>Projection mappers, consulted in exactly this order when no
/// TypeMap applies to a (source, destination) member pair:
/// assignable, enum, enumerable, nullable, string.
/// Provider note: enum name-mapping, Enum.Parse, and ToString/Parse calls
/// are expression-tree method calls. They run against in-memory providers
/// and against providers that translate those calls; a provider that
/// cannot translate one will fail at query execution, which is the
/// provider's own translation limit, not a silent client-side fallback —
/// the builder never evaluates a value itself to work around it.</summary>
public static class ProjectionMappers
{
    internal static readonly IProjectionMapper[] Ordered =
    {
        new AssignableProjectionMapper(),
        new EnumProjectionMapper(),
        new EnumerableProjectionMapper(),
        new NullableProjectionMapper(),
        new StringProjectionMapper(),
    };

    internal static bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result)
    {
        foreach (var mapper in Ordered)
        {
            if (mapper.TryConvert(source, sourceType, destinationType, context, out result)) return true;
        }
        result = null;
        return false;
    }

    internal static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
        var iface = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return iface?.GetGenericArguments()[0];
    }

    internal static bool IsDictionaryType(Type type) =>
        type != typeof(string) && typeof(IDictionary).IsAssignableFrom(type)
        || (type.IsGenericType && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)));

    internal static bool IsNumeric(Type type) => Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) switch
    {
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
            or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => true,
        _ => false,
    };

    /// <summary>Destination assignable from source: pass through (with a
    /// Convert when the static types differ). Also covers implicit numeric
    /// widening (int to long, int to double, ...) via Convert, which is
    /// the expression-tree form of the same widening.</summary>
    public sealed class AssignableProjectionMapper : IProjectionMapper
    {
        public bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result)
        {
            if (destinationType.IsAssignableFrom(sourceType))
            {
                result = source.Type == destinationType ? source : Expression.Convert(source, destinationType);
                return true;
            }
            if (IsNumeric(sourceType) && IsNumeric(destinationType) && sourceType != typeof(bool) && destinationType != typeof(bool))
            {
                result = Expression.Convert(source, destinationType);
                return true;
            }
            result = null;
            return false;
        }
    }

    /// <summary>Enum conversions: enum to enum (matched by member name, with
    /// a numeric cast as the fallback for values with no name match),
    /// enum to string, string to enum, and enum to/from its underlying
    /// numeric type. Nullable-wrapped enums are handled here (before the
    /// nullable mapper, per registry order) by unwrapping, mapping the
    /// underlying value, and re-wrapping with a null check.</summary>
    public sealed class EnumProjectionMapper : IProjectionMapper
    {
        public bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result)
        {
            var srcUnder = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
            var dstUnder = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
            bool srcIsEnum = srcUnder.IsEnum;
            bool dstIsEnum = dstUnder.IsEnum;
            bool otherIsStringOrNumeric = IsNumeric(srcUnder) || IsNumeric(dstUnder) || srcUnder == typeof(string) || dstUnder == typeof(string);
            if (!(srcIsEnum || dstIsEnum) || !otherIsStringOrNumeric && !(srcIsEnum && dstIsEnum))
            {
                // Enum-to-enum is covered by the first clause; anything else
                // needs one side to be an enum and the other a string/number.
                if (!(srcIsEnum && dstIsEnum)) { result = null; return false; }
            }
            if (srcIsEnum && dstIsEnum) { }
            else if (!((srcIsEnum || dstIsEnum) && otherIsStringOrNumeric)) { result = null; return false; }

            var srcNullable = Nullable.GetUnderlyingType(sourceType) != null;
            var dstNullable = Nullable.GetUnderlyingType(destinationType) != null;
            var unwrapped = srcNullable ? (Expression)Expression.Property(source, "Value") : source;

            Expression core;
            if (srcIsEnum && dstIsEnum)
            {
                core = Expression.Convert(unwrapped, dstUnder);
                var dstNames = new HashSet<string>(Enum.GetNames(dstUnder));
                foreach (var value in Enum.GetValues(srcUnder).Cast<object>().Reverse())
                {
                    var name = Enum.GetName(srcUnder, value)!;
                    if (!dstNames.Contains(name)) continue;
                    var dstValue = Enum.Parse(dstUnder, name);
                    core = Expression.Condition(
                        Expression.Equal(unwrapped, Expression.Constant(value, srcUnder)),
                        Expression.Constant(dstValue, dstUnder),
                        core);
                }
            }
            else if (srcIsEnum && dstUnder == typeof(string))
            {
                core = Expression.Call(Expression.Convert(unwrapped, typeof(object)), typeof(object).GetMethod("ToString")!);
            }
            else if (srcUnder == typeof(string) && dstIsEnum)
            {
                var parse = Expression.Call(typeof(Enum), nameof(Enum.Parse), null,
                    Expression.Constant(dstUnder), unwrapped);
                core = Expression.Convert(parse, dstUnder);
            }
            else if (srcIsEnum && IsNumeric(dstUnder))
            {
                core = Expression.Convert(unwrapped, dstUnder);
            }
            else if (IsNumeric(srcUnder) && dstIsEnum)
            {
                core = Expression.Convert(unwrapped, dstUnder);
            }
            else { result = null; return false; }

            if (srcNullable && dstNullable)
                result = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                    Expression.Constant(null, destinationType), Expression.Convert(core, destinationType));
            else if (srcNullable)
                result = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                    Expression.Default(destinationType), core);
            else if (dstNullable)
                result = Expression.Convert(core, destinationType);
            else
                result = core;
            return true;
        }
    }

    /// <summary>Collection projection: source enumerable elements are
    /// projected element-by-element (through the builder, so element
    /// TypeMaps apply) and the result is materialized to the destination
    /// collection type (array via ToArray, List/ICollection/IList via
    /// ToList, IEnumerable via Select, anything with an
    /// IEnumerable-element constructor via that constructor). A null
    /// source collection yields null, or an empty collection when the
    /// configuration has AllowNullCollections off. Strings and
    /// dictionaries are excluded: strings are scalar, and dictionary
    /// projection is not supported in this stage (the builder then
    /// reports the pair as unmapped rather than guessing).</summary>
    public sealed class EnumerableProjectionMapper : IProjectionMapper
    {
        public bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result)
        {
            if (sourceType == typeof(string) || IsDictionaryType(sourceType) || IsDictionaryType(destinationType))
            {
                result = null; return false;
            }
            var srcElem = GetEnumerableElementType(sourceType);
            var dstElem = GetEnumerableElementType(destinationType);
            if (srcElem == null || dstElem == null) { result = null; return false; }

            var elemParam = Expression.Parameter(srcElem, "e");
            var elemValue = context.BuildValue(elemParam, srcElem, dstElem);
            var select = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select),
                new[] { srcElem, dstElem }, source, Expression.Lambda(elemValue, elemParam));
            var materialized = Materialize(select, dstElem, destinationType);
            if (materialized == null) { result = null; return false; }

            if (!NullSafeRewriter.CanBeNull(sourceType)) { result = materialized; return true; }
            var allowNull = context.Configuration.AllowNullCollections;
            Expression nullBranch;
            if (allowNull)
            {
                nullBranch = Expression.Constant(null, destinationType);
            }
            else
            {
                var empty = Expression.Call(typeof(Enumerable), nameof(Enumerable.Empty), new[] { dstElem });
                nullBranch = Materialize(empty, dstElem, destinationType)!;
            }
            result = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                nullBranch, materialized);
            return true;
        }

        internal static Expression? Materialize(Expression iterable, Type dstElem, Type destinationType)
        {
            if (destinationType.IsArray)
                return Expression.Call(typeof(Enumerable), nameof(Enumerable.ToArray), new[] { dstElem }, iterable);
            if (destinationType.IsAssignableFrom(typeof(List<>).MakeGenericType(dstElem)))
                return Expression.Convert(
                    Expression.Call(typeof(Enumerable), nameof(Enumerable.ToList), new[] { dstElem }, iterable),
                    destinationType);
            if (destinationType.IsAssignableFrom(typeof(IEnumerable<>).MakeGenericType(dstElem)))
                return Expression.Convert(iterable, destinationType);
            var ctor = destinationType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
            if (ctor != null) return Expression.New(ctor, iterable);
            if (destinationType.IsAssignableFrom(typeof(List<>).MakeGenericType(dstElem)))
                return Expression.Convert(
                    Expression.Call(typeof(Enumerable), nameof(Enumerable.ToList), new[] { dstElem }, iterable),
                    destinationType);
            return null;
        }
    }

    /// <summary>Nullable source and/or destination: unwrap the source,
    /// map the underlying value through the builder, and re-wrap with a
    /// null check. A null nullable source yields null for a nullable
    /// destination and the destination default for a non-nullable one.</summary>
    public sealed class NullableProjectionMapper : IProjectionMapper
    {
        public bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result)
        {
            var srcUnder = Nullable.GetUnderlyingType(sourceType);
            var dstUnder = Nullable.GetUnderlyingType(destinationType);
            if (srcUnder == null && dstUnder == null) { result = null; return false; }
            var srcValueType = srcUnder ?? sourceType;
            var dstValueType = dstUnder ?? destinationType;
            var unwrapped = srcUnder != null ? (Expression)Expression.Property(source, "Value") : source;
            var mapped = context.BuildValue(unwrapped, srcValueType, dstValueType);
            if (mapped.Type != dstValueType) mapped = Expression.Convert(mapped, dstValueType);
            if (srcUnder != null && dstUnder != null)
                result = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                    Expression.Constant(null, destinationType), Expression.Convert(mapped, destinationType));
            else if (srcUnder != null)
                result = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                    Expression.Default(destinationType), mapped);
            else
                result = Expression.Convert(mapped, destinationType);
            return true;
        }
    }

    /// <summary>String conversions: any non-string source to string via
    /// ToString, and a string source to a destination type that exposes a
    /// static Parse(string) method (numeric, bool, DateTime, Guid, ...).
    /// Enum sides are handled by the enum mapper earlier in the order.</summary>
    public sealed class StringProjectionMapper : IProjectionMapper
    {
        public bool TryConvert(Expression source, Type sourceType, Type destinationType, ProjectionBuildContext context, out Expression? result)
        {
            if (destinationType == typeof(string) && sourceType != typeof(string))
            {
                if (!NullSafeRewriter.CanBeNull(sourceType))
                {
                    result = Expression.Call(Expression.Convert(source, typeof(object)), typeof(object).GetMethod("ToString")!);
                    return true;
                }
                result = Expression.Condition(Expression.Equal(source, Expression.Constant(null, sourceType)),
                    Expression.Constant(null, typeof(string)),
                    Expression.Call(Expression.Convert(source, typeof(object)), typeof(object).GetMethod("ToString")!));
                return true;
            }
            if (sourceType == typeof(string) && destinationType != typeof(string))
            {
                var parse = destinationType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (parse != null && parse.ReturnType == destinationType)
                {
                    result = Expression.Call(parse, source);
                    return true;
                }
            }
            result = null;
            return false;
        }
    }
}
