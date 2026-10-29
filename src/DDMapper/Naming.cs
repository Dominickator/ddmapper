using System.Text;

namespace AutoMapper;

/// <summary>Controls which destination members validation checks.</summary>
public enum MemberList
{
    /// <summary>Every writable destination member must be mapped or ignored.</summary>
    Destination,
    /// <summary>Every readable source member must be mapped or ignored.</summary>
    Source,
    /// <summary>No unmapped-member validation.</summary>
    None,
}

/// <summary>Strategy for turning a member name into comparable parts.</summary>
public interface INamingConvention
{
    /// <summary>Splits a member name into its word parts.</summary>
    string[] Split(string name);

    /// <summary>Reassembles word parts into a member name.</summary>
    string Join(string[] parts);
}

/// <summary>PascalCase splitting: "CustomerName" becomes ["Customer", "Name"].</summary>
public sealed class PascalCaseNamingConvention : INamingConvention
{
    public static readonly PascalCaseNamingConvention Instance = new();
    public string[] Split(string name)
    {
        if (string.IsNullOrEmpty(name)) return Array.Empty<string>();
        var parts = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && current.Length > 0 &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            current.Append(c);
        }
        parts.Add(current.ToString());
        return parts.ToArray();
    }
    public string Join(string[] parts) => string.Concat(parts.Select(p =>
        p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
}

/// <summary>Exact, case-sensitive name matching; no splitting.</summary>
public sealed class ExactMatchNamingConvention : INamingConvention
{
    public static readonly ExactMatchNamingConvention Instance = new();
    public string[] Split(string name) => string.IsNullOrEmpty(name) ? Array.Empty<string>() : new[] { name };
    public string Join(string[] parts) => string.Concat(parts);
}

/// <summary>snake_case splitting: "customer_name" becomes ["customer", "name"].</summary>
public sealed class LowerUnderscoreNamingConvention : INamingConvention
{
    public static readonly LowerUnderscoreNamingConvention Instance = new();
    public string[] Split(string name) =>
        string.IsNullOrEmpty(name) ? Array.Empty<string>() : name.Split('_', StringSplitOptions.RemoveEmptyEntries);
    public string Join(string[] parts) => string.Join("_", parts.Select(p => p.ToLowerInvariant()));
}

/// <summary>Shared name helpers used by member matching and flattening.</summary>
public static class NameHelpers
{
    /// <summary>Converts a snake_case name to PascalCase.</summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    /// <summary>True when two names match ignoring case.</summary>
    public static bool NamesEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a PascalCase name into words.</summary>
    public static string[] SplitPascal(string name) => PascalCaseNamingConvention.Instance.Split(name);
}
