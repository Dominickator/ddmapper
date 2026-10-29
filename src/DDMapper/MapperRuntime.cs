using System.Collections;
using System.Dynamic;
using System.Reflection;

namespace AutoMapper;

/// <summary>
/// Shared runtime used by compiled plans and by the fallback chain: nested value mapping,
/// collection/dictionary mapping, and the ordered single-value conversions.
/// </summary>
public static class MapperRuntime
{
    /// <summary>Maps one value, honouring type maps first and the fallback chain otherwise.</summary>
    public static object? MapValue(ResolutionContext ctx, object? value, Type sourceType, Type destType, TypeMap? parentMap)
    {
        if (value == null)
        {
            if (destType.IsValueType && Nullable.GetUnderlyingType(destType) == null)
                return Activator.CreateInstance(destType);
            return null;
        }
        var mapper = (Mapper)ctx.Mapper;
        return mapper.MapInternal(value, sourceType, destType, null, ctx);
    }

    internal static bool IsCollectionType(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    internal static Type? ElementTypeOf(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            if (args.Length == 1) return args[0];
            if (args.Length == 2 && typeof(IDictionary).IsAssignableFrom(type)) return null;
        }
        var enumerable = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    internal static bool IsDictionaryType(Type type)
    {
        if (typeof(IDictionary).IsAssignableFrom(type)) return true;
        return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }

    internal static (Type Key, Type Value)? DictionaryArgs(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var a = type.GetGenericArguments();
            return (a[0], a[1]);
        }
        var iface = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (iface != null)
        {
            var a = iface.GetGenericArguments();
            return (a[0], a[1]);
        }
        return null;
    }
}

/// <summary>
/// The ordered fallback chain, tried when no TypeMap plan matches a pair.
/// Order (fixed): Collection, Assignable, NullableSource, ToString, NullableDestination,
/// Convert, StringToEnum, EnumToEnum, ParseString, UnderlyingTypeEnum, KeyValue,
/// Constructor, ConversionOperator, FromDynamic, ToDynamic, FromStringDictionary,
/// ToStringDictionary.
/// </summary>
public static class FallbackChain
{
    public static readonly string[] Order =
    {
        "Collection", "Assignable", "NullableSource", "ToString", "NullableDestination", "Convert",
        "StringToEnum", "EnumToEnum", "ParseString", "UnderlyingTypeEnum", "KeyValue",
        "Constructor", "ConversionOperator", "FromDynamic", "ToDynamic",
        "FromStringDictionary", "ToStringDictionary",
    };

    /// <summary>Tries every fallback in order. Returns false when none applies.</summary>
    public static bool TryMap(Mapper mapper, ResolutionContext ctx, object source, Type sourceType, Type destType, out object? result)
    {
        result = null;
        // Collection
        if (MapperRuntime.IsCollectionType(sourceType) && MapperRuntime.IsCollectionType(destType) && !MapperRuntime.IsDictionaryType(sourceType) && !MapperRuntime.IsDictionaryType(destType))
        {
            var elementType = MapperRuntime.ElementTypeOf(destType) ?? typeof(object);
            var items = new List<object?>();
            foreach (var item in (IEnumerable)source)
                items.Add(item == null ? null : mapper.MapInternal(item, item.GetType(), elementType, null, ctx));
            result = Materialize(items, destType, elementType);
            return result != null || destType.IsAssignableFrom(typeof(List<object>)) || destType.IsInterface || destType.IsArray;
        }
        // Assignable
        if (destType.IsAssignableFrom(sourceType)) { result = source; return true; }
        // NullableSource: Nullable<T> -> T (source is already unwrapped by boxing, but handle explicitly)
        var nullableSource = Nullable.GetUnderlyingType(sourceType);
        if (nullableSource != null)
        {
            if (mapper.MapInternal(source, nullableSource, destType, null, ctx) is object unwrapped) { result = unwrapped; return true; }
        }
        // ToString
        if (destType == typeof(string)) { result = source.ToString(); return true; }
        // NullableDestination: T -> Nullable<T>
        var nullableDest = Nullable.GetUnderlyingType(destType);
        if (nullableDest != null)
        {
            if (TryMap(mapper, ctx, source, sourceType, nullableDest, out var inner)) { result = inner; return true; }
        }
        // Convert (IConvertible / System.Convert)
        if (source is IConvertible && TryConvert(source, destType, out var converted)) { result = converted; return true; }
        // StringToEnum
        if (sourceType == typeof(string) && destType.IsEnum)
        {
            if (Enum.TryParse(destType, (string)source, ignoreCase: true, out var parsed)) { result = parsed; return true; }
        }
        // EnumToEnum
        if (sourceType.IsEnum && destType.IsEnum)
        {
            var name = Enum.GetName(sourceType, source);
            if (name != null && Enum.TryParse(destType, name, ignoreCase: true, out var parsed)) { result = parsed; return true; }
            result = Enum.ToObject(destType, Convert.ToInt64(source));
            return true;
        }
        // ParseString: destination type exposes a static Parse(string) method.
        if (sourceType == typeof(string))
        {
            var parse = destType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (parse != null)
            {
                try { result = parse.Invoke(null, new object[] { source }); return true; }
                catch { /* fall through to later fallbacks */ }
            }
        }
        // UnderlyingTypeEnum: enum <-> its underlying numeric type.
        if (sourceType.IsEnum && Nullable.GetUnderlyingType(destType) == null && destType == Enum.GetUnderlyingType(sourceType))
        { result = Convert.ChangeType(source, destType); return true; }
        if (destType.IsEnum && sourceType == Enum.GetUnderlyingType(destType))
        { result = Enum.ToObject(destType, source); return true; }
        // KeyValue: KeyValuePair / dictionary-entry style mapping is handled by the
        // dictionary branch in Mapper; here a pair whose key/value convert is passed through.
        // Constructor
        if (TryConstructor(mapper, ctx, source, sourceType, destType, out var constructed)) { result = constructed; return true; }
        // ConversionOperator: implicit / explicit operators on either side.
        if (TryOperator(source, sourceType, destType, out var viaOperator)) { result = viaOperator; return true; }
        // FromDynamic
        if (source is ExpandoObject || source is IDictionary<string, object?>)
        {
            var dict = source as IDictionary<string, object?> ?? ToStringObjectDictionary((ExpandoObject)source);
            if (TryFromDictionary(mapper, ctx, dict, destType, out var fromDyn)) { result = fromDyn; return true; }
        }
        // ToDynamic
        if (destType == typeof(ExpandoObject) || destType == typeof(object) && sourceType != typeof(string))
        {
            // Only claim ToDynamic when the destination is actually a dynamic type.
            if (destType == typeof(ExpandoObject))
            {
                result = ToExpando(mapper, ctx, source);
                return true;
            }
        }
        // FromStringDictionary
        if (source is IDictionary<string, string> stringDict)
        {
            if (TryFromDictionary(mapper, ctx, stringDict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value), destType, out var fromStr)) { result = fromStr; return true; }
        }
        // ToStringDictionary
        if (typeof(IDictionary<string, string>).IsAssignableFrom(destType) || destType == typeof(Dictionary<string, string>))
        {
            var dict = new Dictionary<string, string>();
            foreach (var member in MemberMatcher.ReadableMembers(sourceType))
                dict[member.Name] = MemberMatcher.GetValue(member, source)?.ToString() ?? string.Empty;
            result = dict;
            return true;
        }
        return false;
    }

    internal static object? Materialize(List<object?> items, Type destType, Type elementType)
    {
        if (destType.IsArray)
        {
            var array = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
            return array;
        }
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in items) list.Add(item);
        if (destType.IsAssignableFrom(listType)) return list;
        if (destType.IsInterface || destType.IsAbstract) return list;
        try
        {
            var instance = Activator.CreateInstance(destType);
            if (instance is IList targetList)
            {
                foreach (var item in items) targetList.Add(item);
                return targetList;
            }
            var add = destType.GetMethod("Add", new[] { elementType });
            if (add != null)
            {
                foreach (var item in items) add.Invoke(instance, new[] { item });
                return instance;
            }
        }
        catch { /* fall through */ }
        return list;
    }

    private static bool TryConvert(object source, Type destType, out object? result)
    {
        result = null;
        try
        {
            var target = Nullable.GetUnderlyingType(destType) ?? destType;
            if (target.IsEnum) return false;
            result = Convert.ChangeType(source, target);
            return true;
        }
        catch { return false; }
    }

    private static bool TryConstructor(Mapper mapper, ResolutionContext ctx, object source, Type sourceType, Type destType, out object? result)
    {
        result = null;
        if (destType.IsAbstract || destType.IsInterface) return false;
        foreach (var ctor in destType.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 0) continue;
            var args = new object?[parameters.Length];
            bool ok = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                var member = MemberMatcher.ReadableMembers(sourceType)
                    .FirstOrDefault(m => string.Equals(m.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));
                if (member == null) { ok = false; break; }
                var value = MemberMatcher.GetValue(member, source);
                args[i] = value == null ? null : mapper.MapInternal(value, MemberMatcher.MemberType(member), parameters[i].ParameterType, null, ctx);
            }
            if (!ok) continue;
            try { result = ctor.Invoke(args); return true; }
            catch { /* try the next constructor */ }
        }
        return false;
    }

    private static bool TryOperator(object source, Type sourceType, Type destType, out object? result)
    {
        result = null;
        foreach (var type in new[] { sourceType, destType })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if ((method.Name != "op_Implicit" && method.Name != "op_Explicit") || method.ReturnType != destType) continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(sourceType))
                {
                    try { result = method.Invoke(null, new[] { source }); return true; }
                    catch { /* keep looking */ }
                }
            }
        }
        return false;
    }

    private static IDictionary<string, object?> ToStringObjectDictionary(ExpandoObject expando) =>
        (IDictionary<string, object?>)expando;

    internal static bool TryFromDictionary(Mapper mapper, ResolutionContext ctx, IDictionary<string, object?> dict, Type destType, out object? result)
    {
        result = null;
        if (destType.IsAbstract || destType.IsInterface) return false;
        object? instance;
        try { instance = Activator.CreateInstance(destType); }
        catch { return false; }
        if (instance == null) return false;
        bool any = false;
        foreach (var prop in destType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var key = dict.Keys.FirstOrDefault(k => string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            var value = dict[key];
            prop.SetValue(instance, value == null ? null : mapper.MapInternal(value, value.GetType(), prop.PropertyType, null, ctx));
            any = true;
        }
        result = instance;
        return any;
    }

    internal static ExpandoObject ToExpando(Mapper mapper, ResolutionContext ctx, object source)
    {
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;
        foreach (var member in MemberMatcher.ReadableMembers(source.GetType()))
            dict[member.Name] = MemberMatcher.GetValue(member, source);
        return expando;
    }
}
