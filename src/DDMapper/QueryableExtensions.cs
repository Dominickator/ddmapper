using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper.QueryableExtensions;

/// <summary>IQueryable projection (Stage 2, original code): turns a
/// configured map into a Select expression so the projection runs in
/// the query provider instead of materializing source objects first.
///
/// Two call shapes are provided because C# cannot partially infer
/// type arguments: <c>ProjectTo&lt;TDestination&gt;</c> over a plain
/// <see cref="IQueryable"/> (element type taken from the query at
/// runtime), and <c>ProjectTo&lt;TSource, TDestination&gt;</c> over an
/// <see cref="IQueryable{T}"/> when both types are known statically.
/// Both build the identical expression.
///
/// Provider limits (see ProjectionBuilder for the full list):
///  - Only translatable members project. Condition/PreCondition
///    delegates, resolvers, member converters, and whole-type converters
///    are rejected with AutoMapperProjectionException at build time.
///  - The resulting expression is a plain Select over the source query;
///    nothing is evaluated on the client while building it, and no
///    client-evaluation fallback is ever added.
///  - Parameters: the dictionary overload substitutes supplied values
///    for closure-captured variables of the same name in MapFrom
///    expressions, so a provider sees plain constants instead of a
///    closure object. The anonymous-object overload is the same
///    dictionary built from the object's public properties.</summary>
public static class QueryableExtensions
{
    // ---- ProjectTo<TDestination> over plain IQueryable -------------------------

    /// <summary>Projects each source element to TDestination using the
    /// configuration's map for the query's element type to TDestination.</summary>
    public static IQueryable<TDestination> ProjectTo<TDestination>(
        this IQueryable source,
        IConfigurationProvider configuration)
        => ProjectTo<TDestination>(source, configuration, parameters: (IDictionary<string, object?>?)null);

    /// <summary>Projects with explicit mapping-operation parameters.
    /// Each entry replaces a closure-captured variable of the same name
    /// (case-insensitive) inside MapFrom expressions; entries that match
    /// no captured variable are ignored.</summary>
    public static IQueryable<TDestination> ProjectTo<TDestination>(
        this IQueryable source,
        IConfigurationProvider configuration,
        IDictionary<string, object?>? parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configuration);
        var elementType = source.ElementType;
        var build = typeof(AutoMapper.ProjectionBuilder).GetMethod(nameof(AutoMapper.ProjectionBuilder.Build))!
            .MakeGenericMethod(elementType, typeof(TDestination));
        LambdaExpression projection;
        try
        {
            projection = (LambdaExpression)build.Invoke(null, new object?[] { configuration, ToReadOnly(parameters) })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Reflection wraps build failures; surface the real
            // projection/mapping exception so callers see its type.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        var select = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.Select) && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsGenericType
                && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            .MakeGenericMethod(elementType, typeof(TDestination));
        var call = Expression.Call(select, source.Expression, projection);
        return source.Provider.CreateQuery<TDestination>(call);
    }

    /// <summary>Projects with parameters supplied as an anonymous object
    /// (or any object whose public properties become the parameter
    /// dictionary). A null parameters object behaves like the
    /// parameterless overload.</summary>
    public static IQueryable<TDestination> ProjectTo<TDestination>(
        this IQueryable source,
        IConfigurationProvider configuration,
        object? parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configuration);
        if (parameters == null) return ProjectTo<TDestination>(source, configuration, (IDictionary<string, object?>?)null);
        if (parameters is IDictionary<string, object?> dict) return ProjectTo<TDestination>(source, configuration, dict);
        return ProjectTo<TDestination>(source, configuration, ToDictionary(parameters));
    }

    // ---- ProjectTo<TSource, TDestination> over IQueryable<TSource> ---------------

    /// <summary>Projects each source element to TDestination using the
    /// configuration's map for (TSource, TDestination).</summary>
    public static IQueryable<TDestination> ProjectTo<TSource, TDestination>(
        this IQueryable<TSource> source,
        IConfigurationProvider configuration)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configuration);
        return source.Select(AutoMapper.ProjectionBuilder.Build<TSource, TDestination>(configuration));
    }

    /// <summary>Projects with explicit mapping-operation parameters (see
    /// the plain-IQueryable overload for their meaning).</summary>
    public static IQueryable<TDestination> ProjectTo<TSource, TDestination>(
        this IQueryable<TSource> source,
        IConfigurationProvider configuration,
        IDictionary<string, object?>? parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configuration);
        return source.Select(AutoMapper.ProjectionBuilder.Build<TSource, TDestination>(configuration, ToReadOnly(parameters)));
    }

    /// <summary>Projects with parameters supplied as an anonymous object.</summary>
    public static IQueryable<TDestination> ProjectTo<TSource, TDestination>(
        this IQueryable<TSource> source,
        IConfigurationProvider configuration,
        object? parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configuration);
        if (parameters == null) return ProjectTo<TSource, TDestination>(source, configuration);
        if (parameters is IDictionary<string, object?> dict) return ProjectTo<TSource, TDestination>(source, configuration, dict);
        return ProjectTo<TSource, TDestination>(source, configuration, ToDictionary(parameters));
    }

    /// <summary>Exposes the built projection expression without applying
    /// it, for callers that need to inspect or compose it (for example
    /// in tests or in a provider-specific pipeline).</summary>
    public static Expression<Func<TSource, TDestination>> BuildProjection<TSource, TDestination>(
        IConfigurationProvider configuration,
        IDictionary<string, object?>? parameters = null) =>
        AutoMapper.ProjectionBuilder.Build<TSource, TDestination>(configuration, ToReadOnly(parameters));

    private static IReadOnlyDictionary<string, object?>? ToReadOnly(IDictionary<string, object?>? parameters) =>
        parameters == null ? null
        : parameters is IReadOnlyDictionary<string, object?> readOnly ? readOnly
        : new Dictionary<string, object?>(parameters);

    private static Dictionary<string, object?> ToDictionary(object parameters)
    {
        var built = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead) built[prop.Name] = prop.GetValue(parameters);
        }
        return built;
    }
}
