using System.Reflection;

namespace AutoMapper;

/// <summary>Finds a source member chain for a destination member name, honouring naming
/// conventions, prefix/postfix stripping, and flattening (Customer.Name -&gt; CustomerName).</summary>
public static class MemberMatcher
{
    public static MemberInfo[]? FindSourceChain(Type sourceType, string destinationName, MapperConfiguration configuration)
    {
        // 1. Exact / case-insensitive direct match, after stripping configured prefixes/postfixes.
        foreach (var candidate in StrippedNames(destinationName, configuration, destination: true))
        {
            var direct = FindMember(sourceType, candidate, configuration);
            if (direct != null) return new[] { direct };
        }
        // Also try the destination name itself against source members with stripped source names.
        var direct2 = FindMemberAllowingStrippedSource(sourceType, destinationName, configuration);
        if (direct2 != null) return new[] { direct2 };

        // 2. Flattening: split the destination name into words and walk a member chain.
        var words = NameHelpers.SplitPascal(destinationName);
        if (words.Length > 1)
        {
            // TryChain already explores every split of the words into a member chain.
            var chain = TryChain(sourceType, words, configuration);
            if (chain != null) return chain;
        }

        // 3. Underscore source: source "customer_name" maps to destination "CustomerName".
        var pascal = NameHelpers.ToPascalCase(destinationName);
        if (!string.Equals(pascal, destinationName, StringComparison.Ordinal))
        {
            var m = FindMember(sourceType, pascal, configuration);
            if (m != null) return new[] { m };
        }
        // Source member whose snake_case converts to the destination name.
        foreach (var member in ReadableMembers(sourceType))
        {
            if (string.Equals(NameHelpers.ToPascalCase(member.Name), destinationName, StringComparison.OrdinalIgnoreCase))
                return new[] { member };
            // Destination convention: split destination name and compare underscore-joined source name.
            if (configuration.DestinationNamingConvention is LowerUnderscoreNamingConvention)
            {
                if (string.Equals(member.Name, string.Join("_", words).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                    return new[] { member };
            }
        }
        return null;
    }

    private static MemberInfo[]? TryChain(Type currentType, string[] words, MapperConfiguration configuration)
    {
        // Greedy longest-first walk: at each level, consume the longest prefix of remaining
        // words that names a member of the current type.
        var chain = new List<MemberInfo>();
        int index = 0;
        var type = currentType;
        while (index < words.Length)
        {
            MemberInfo? found = null;
            int consumed = 0;
            for (int take = words.Length - index; take >= 1; take--)
            {
                var candidate = string.Concat(words.Skip(index).Take(take));
                found = FindMember(type, candidate, configuration)
                    ?? FindMemberAllowingStrippedSource(type, candidate, configuration);
                if (found != null) { consumed = take; break; }
            }
            if (found == null) return null;
            chain.Add(found);
            index += consumed;
            type = MemberType(found);
            if (index < words.Length && (type == typeof(string) || type.IsPrimitive)) return null;
        }
        return chain.Count > 1 ? chain.ToArray() : null;
    }

    private static IEnumerable<string> StrippedNames(string name, MapperConfiguration configuration, bool destination)
    {
        yield return name;
        var prefixes = destination ? configuration.DestinationPrefixes : configuration.SourcePrefixes;
        var postfixes = destination ? configuration.DestinationPostfixes : configuration.SourcePostfixes;
        foreach (var p in prefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase) && name.Length > p.Length)
                yield return name.Substring(p.Length);
        foreach (var p in postfixes)
            if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase) && name.Length > p.Length)
                yield return name.Substring(0, name.Length - p.Length);
    }

    private static MemberInfo? FindMember(Type type, string name, MapperConfiguration configuration)
    {
        foreach (var m in ReadableMembers(type))
        {
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            // Source naming convention: e.g. a snake_case source member compared in PascalCase.
            if (configuration.SourceNamingConvention is LowerUnderscoreNamingConvention &&
                string.Equals(NameHelpers.ToPascalCase(m.Name), name, StringComparison.OrdinalIgnoreCase))
                return m;
        }
        return null;
    }

    private static MemberInfo? FindMemberAllowingStrippedSource(Type type, string name, MapperConfiguration configuration)
    {
        foreach (var m in ReadableMembers(type))
            foreach (var stripped in StrippedNames(m.Name, configuration, destination: false))
                if (string.Equals(stripped, name, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    public static IEnumerable<MemberInfo> ReadableMembers(Type type)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.GetMethod != null) yield return p;
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            yield return f;
    }

    public static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        _ => typeof(object),
    };

    public static object? GetValue(MemberInfo member, object target) => member switch
    {
        PropertyInfo p => p.GetValue(target),
        FieldInfo f => f.GetValue(target),
        _ => null,
    };

    public static void SetValue(MemberInfo member, object target, object? value)
    {
        switch (member)
        {
            case PropertyInfo p when p.CanWrite: p.SetValue(target, value); break;
            case FieldInfo f when !f.IsInitOnly: f.SetValue(target, value); break;
        }
    }

    public static bool CanWrite(MemberInfo member) => member switch
    {
        PropertyInfo p => p.CanWrite && p.GetSetMethod() != null,
        FieldInfo f => !f.IsInitOnly,
        _ => false,
    };
}
