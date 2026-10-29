using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace AutoMapper;

/// <summary>Raised when a configured map cannot be turned into a query
/// expression. This is a deliberate failure, not a fallback: the builder
/// never silently evaluates a member on the client side to paper over a
/// member it cannot translate.</summary>
public class AutoMapperProjectionException : AutoMapperMappingException
{
    public AutoMapperProjectionException(string message) : base(message) { }
}

/// <summary>Builds a LINQ projection expression
/// <c>Expression&lt;Func&lt;TSource, TDestination&gt;&gt;</c> from a
/// configuration's TypeMap, reusing the Stage 1 TypeMap model and its
/// conventional member matching (flattening chains are already resolved
/// on the TypeMap at seal time). Original code, built from the TypeMap
/// model and general mapper knowledge only.
///
/// Supported in this stage:
///  - Parameterless-constructor and parameterized-constructor destinations
///    (member-init for the remaining members; constructor-covered members
///    are not assigned twice, matching Stage 1 plan behaviour).
///  - ForMember custom expressions with a single source parameter, rebased
///    onto the projection source. Two-parameter custom expressions (which
///    need a destination instance that does not exist yet in a query) are
///    rejected.
///  - Ignore, NullSubstitute, flattening, nested objects, and collections.
///  - Polymorphic dispatch over a base map's included-derived maps.
/// Rejected with <see cref="AutoMapperProjectionException"/> (never
/// silently degraded): Condition / PreCondition delegates, resolver
/// instances, member converters, whole-type ConvertUsing / ConstructUsing
/// functions, UseDestinationValue / MapAtRuntime members, and destination
/// paths whose root collides with a mapped property.
/// Not supported in this stage: dictionary projection, interface/abstract
/// destinations, and BeforeMap/AfterMap hooks (hooks run at Map time
/// around an existing destination and have no query form).
///
/// Recursion: a self-referential pair (for example a tree node mapping
/// to a node DTO) cannot expand forever in a finite expression tree.
/// Re-entry of the same pair is capped: the map's MaxDepth, else the
/// configuration default, else 5 levels. Deeper occurrences project to
/// the destination default (null for reference types). Set MaxDepth on
/// the map to raise or lower the cap.
///
/// Parameters: when the ProjectTo parameters overload is used, closure
/// values in custom expressions are replaced by the supplied values,
/// matched by captured-variable name, so the resulting tree carries
/// plain constants instead of a closure object (see
/// ProjectionVisitors.ClosureParameterizer). Parameterized builds are
/// not cached; parameterless builds are cached per configuration
/// (ConditionalWeakTable, so a configuration can still be collected).
/// </summary>
public static class ProjectionBuilder
{
    private const int DefaultRecursiveCap = 5;
    private static readonly ConditionalWeakTable<IConfigurationProvider, ConcurrentDictionary<(Type, Type), LambdaExpression>> Cache = new();

    public static Expression<Func<TSource, TDestination>> Build<TSource, TDestination>(
        IConfigurationProvider configuration, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (parameters == null || parameters.Count == 0)
        {
            var dict = Cache.GetOrCreateValue(configuration);
            if (dict.TryGetValue((typeof(TSource), typeof(TDestination)), out var cached))
                return (Expression<Func<TSource, TDestination>>)cached;
            var built = BuildUncached<TSource, TDestination>(configuration, null);
            dict[(typeof(TSource), typeof(TDestination))] = built;
            return built;
        }
        return BuildUncached<TSource, TDestination>(configuration, parameters);
    }

    private static Expression<Func<TSource, TDestination>> BuildUncached<TSource, TDestination>(
        IConfigurationProvider configuration, IReadOnlyDictionary<string, object?>? parameters)
    {
        var context = new ProjectionBuildContext(configuration, parameters);
        context.BuildValue = (expr, srcType, dstType) => BuildValue(expr, srcType, dstType, context);
        var sourceParam = Expression.Parameter(typeof(TSource), "src");
        var body = BuildDestination(sourceParam, typeof(TSource), typeof(TDestination), context, topLevel: true);
        if (body.Type != typeof(TDestination)) body = Expression.Convert(body, typeof(TDestination));
        return Expression.Lambda<Func<TSource, TDestination>>(body, sourceParam);
    }

    // ---- destination construction -------------------------------------------------

    private static Expression BuildDestination(Expression sourceExpr, Type sourceType, Type destinationType,
        ProjectionBuildContext context, bool topLevel)
    {
        var typeMap = context.Configuration.FindTypeMap(sourceType, destinationType);
        if (typeMap == null)
        {
            // Polymorphic fallback: maps whose source derives from the
            // requested source and whose destination is the requested
            // destination can still dispatch on runtime type, even when
            // no base map was registered.
            var derived = context.Configuration.TypeMaps.Values
                .Where(m => m.DestinationType == destinationType && sourceType.IsAssignableFrom(m.SourceType) && m.SourceType != sourceType)
                .OrderBy(m => m.SourceType.Name)
                .ToList();
            if (derived.Count == 0)
                throw new AutoMapperMappingException(
                    $"No map is configured from {sourceType.Name} to {destinationType.Name} for projection. " +
                    $"Create a map for this pair before calling ProjectTo, or map the values with the regular mapper instead.");
            return BuildDerivedDispatch(sourceExpr, sourceType, destinationType, derived, context, baseBody: null);
        }

        EnsurePropertyMaps(typeMap, context.Configuration);

        if (typeMap.BeforeMapActions.Count > 0 || typeMap.AfterMapActions.Count > 0
            || typeMap.BeforeMapActionTypes.Count > 0 || typeMap.AfterMapActionTypes.Count > 0)
            throw new AutoMapperProjectionException(
                $"The map from {sourceType.Name} to {destinationType.Name} uses BeforeMap/AfterMap hooks, which run at Map time around an existing destination and have no query form. Remove the hooks for projection, or map the values with the regular mapper instead.");
        if (typeMap.ValueTransformers.Count > 0)
            throw new AutoMapperProjectionException(
                $"The map from {sourceType.Name} to {destinationType.Name} uses value transformers, which are compiled delegates and cannot be translated into a query expression. Remove the transformers for projection, or map the values with the regular mapper instead.");
        if (typeMap.TypeConverterFunc != null || typeMap.TypeConverter != null || typeMap.TypeConverterType != null)
            throw new AutoMapperProjectionException(
                $"The map from {sourceType.Name} to {destinationType.Name} uses a whole-type converter, which cannot be translated into a query expression. Remove the converter for projection, or map the values with the regular mapper instead.");
        if (typeMap.ConstructUsingFunc != null || typeMap.ConstructUsingWithContext != null)
            throw new AutoMapperProjectionException(
                $"The map from {sourceType.Name} to {destinationType.Name} uses a custom construction function, which cannot be translated into a query expression. Give the destination a parameterless constructor (or a constructor whose parameters match source members) for projection.");

        // Included-derived dispatch: runtime type decides the branch.
        var included = typeMap.IncludedDerived
            .Where(p => sourceType.IsAssignableFrom(p.Source) && destinationType.IsAssignableFrom(p.Destination))
            .ToList();
        Expression baseBody;
        if (included.Count > 0)
        {
            // Build the base body first (without re-entering dispatch for
            // this same pair), then wrap it in the dispatch chain.
            baseBody = BuildObject(sourceExpr, sourceType, destinationType, typeMap, context);
            var derivedMaps = included
                .Select(p => context.Configuration.FindTypeMap(p.Source, p.Destination))
                .Where(m => m != null)
                .Select(m => m!)
                .ToList();
            var dispatched = BuildDerivedDispatch(sourceExpr, sourceType, destinationType, derivedMaps, context, baseBody);
            return topLevel ? WrapTopLevelNull(sourceExpr, dispatched, destinationType) : dispatched;
        }

        var body = BuildObject(sourceExpr, sourceType, destinationType, typeMap, context);
        return topLevel ? WrapTopLevelNull(sourceExpr, body, destinationType) : body;
    }

    private static Expression WrapTopLevelNull(Expression sourceExpr, Expression body, Type destinationType)
    {
        if (!NullSafeRewriter.CanBeNull(sourceExpr.Type) || !NullSafeRewriter.CanBeNull(destinationType)) return body;
        return Expression.Condition(Expression.Equal(sourceExpr, Expression.Constant(null, sourceExpr.Type)),
            Expression.Constant(null, destinationType), body);
    }

    private static Expression BuildDerivedDispatch(Expression sourceExpr, Type sourceType, Type destinationType,
        List<TypeMap> derivedMaps, ProjectionBuildContext context, Expression? baseBody)
    {
        Expression fallback = baseBody ?? Expression.Throw(
            Expression.New(
                typeof(AutoMapperMappingException).GetConstructor(new[] { typeof(string) })!,
                Expression.Constant(
                    $"No map is configured from {sourceType.Name} to {destinationType.Name} for projection. " +
                    $"Create a map for this pair before calling ProjectTo, or map the values with the regular mapper instead.")),
            destinationType);
        // Registration order, folded so a later registration is checked
        // first and wins on overlapping derived types.
        foreach (var derived in derivedMaps)
        {
            var convertedSource = derived.SourceType == sourceExpr.Type
                ? sourceExpr
                : Expression.Convert(sourceExpr, derived.SourceType);
            var branch = BuildDestination(convertedSource, derived.SourceType, derived.DestinationType, context, topLevel: false);
            if (branch.Type != destinationType) branch = Expression.Convert(branch, destinationType);
            fallback = Expression.Condition(Expression.TypeIs(sourceExpr, derived.SourceType), branch, fallback);
        }
        return fallback;
    }

    private static Expression BuildObject(Expression sourceExpr, Type sourceType, Type destinationType,
        TypeMap typeMap, ProjectionBuildContext context)
    {
        // The derived-dispatch path builds the base body through here
        // directly, so the hook/transformer rejection must live here too
        // rather than only in BuildDestination.
        if (typeMap.BeforeMapActions.Count > 0 || typeMap.AfterMapActions.Count > 0
            || typeMap.BeforeMapActionTypes.Count > 0 || typeMap.AfterMapActionTypes.Count > 0)
            throw new AutoMapperProjectionException(
                $"The map from {sourceType.Name} to {destinationType.Name} uses BeforeMap/AfterMap hooks, which run at Map time around an existing destination and have no query form.");
        if (typeMap.ValueTransformers.Count > 0)
            throw new AutoMapperProjectionException(
                $"The map from {sourceType.Name} to {destinationType.Name} uses value transformers, which are compiled delegates and cannot be translated into a query expression.");
        // Recursion cap (see the class documentation).
        int cap = typeMap.MaxDepth ?? context.Configuration.MaxDepthDefault;
        if (cap <= 0) cap = DefaultRecursiveCap;
        int already = context.Stack.Count(p => p.Source == sourceType && p.Destination == destinationType);
        if (already >= cap) return Expression.Default(destinationType);
        context.Stack.Add((sourceType, destinationType));
        try
        {
            if (destinationType.IsInterface || destinationType.IsAbstract)
                throw new AutoMapperProjectionException(
                    $"Cannot project to {destinationType.Name} because interface and abstract destinations have no constructor to project into. Project to a concrete type instead.");

            var ctor = PlanBuilder.ChooseConstructor(typeMap);
            if (ctor == null)
                throw new AutoMapperMappingException(
                    $"Cannot project to {destinationType.Name}: it has no parameterless constructor and no constructor whose parameters match the source members of {sourceType.Name}. Give the destination a usable constructor for projection.");
            var ctorParamNames = new HashSet<string>(ctor.GetParameters().Select(p => p.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

            var bindings = new List<MemberBinding>();
            foreach (var pm in typeMap.PropertyMaps)
            {
                if (pm.Ignored || pm.UseDestinationValue) continue;
                if (ctorParamNames.Contains(pm.DestinationName)) continue;
                bindings.Add(Expression.Bind(pm.DestinationProperty, BuildPropertyValue(sourceExpr, sourceType, pm, context)));
            }
            foreach (var binding in BuildPathBindings(sourceExpr, sourceType, destinationType, typeMap, context, bindings.Select(b => b.Member.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)))
                bindings.Add(binding);

            Expression created;
            if (ctor.GetParameters().Length == 0)
            {
                created = Expression.New(destinationType);
            }
            else
            {
                var args = new List<Expression>();
                foreach (var param in ctor.GetParameters())
                {
                    var explicitMap = typeMap.ConstructorMap.Parameters.FirstOrDefault(p =>
                        string.Equals(p.ParameterName, param.Name, StringComparison.OrdinalIgnoreCase));
                    if (explicitMap?.CustomExpression != null)
                    {
                        args.Add(BuildCustomValue(sourceExpr, explicitMap.CustomExpression, param.ParameterType, context));
                    }
                    else
                    {
                        var member = MemberMatcher.ReadableMembers(sourceType)
                            .FirstOrDefault(m => string.Equals(m.Name, param.Name, StringComparison.OrdinalIgnoreCase));
                        if (member == null) { args.Add(Expression.Default(param.ParameterType)); continue; }
                        var raw = Expression.MakeMemberAccess(sourceExpr, member);
                        args.Add(BuildValue(NullSafeRewriter.Rewrite(raw), MemberMatcher.MemberType(member), param.ParameterType, context));
                    }
                }
                created = Expression.New(ctor, args);
            }
            return bindings.Count == 0 ? created : Expression.MemberInit((NewExpression)created, bindings);
        }
        finally
        {
            context.Stack.RemoveAt(context.Stack.Count - 1);
        }
    }

    // ---- one destination member ---------------------------------------------------

    private static Expression BuildPropertyValue(Expression sourceExpr, Type sourceType, PropertyMap pm, ProjectionBuildContext context)
    {
        if (pm.MapAtRuntime)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} is marked to map at runtime, which cannot be translated into a query expression. Configure it with a translatable expression for projection.");
        if (pm.Resolver != null || pm.ResolverType != null)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} uses a value resolver, which cannot be translated into a query expression. Replace it with a translatable MapFrom expression for projection.");
        if (pm.MemberResolver != null || pm.MemberResolverType != null)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} uses a member value resolver, which cannot be translated into a query expression. Replace it with a translatable MapFrom expression for projection.");
        if (pm.TypeConverter != null || pm.ConverterType != null)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} uses a member converter, which cannot be translated into a query expression. Replace it with a translatable MapFrom expression for projection.");
        if (pm.ValueTransformers.Count > 0)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} uses value transformers, which are compiled delegates and cannot be translated into a query expression. Remove the transformers for projection, or map the values with the regular mapper instead.");
        if (context.Configuration.GlobalValueTransformers.Any(r => r.AppliesTo(pm.DestinationType)))
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} is covered by a global value transformer, which is a compiled delegate and cannot be translated into a query expression. Remove the global transformer for projection, or map the values with the regular mapper instead.");
        if (pm.Condition != null)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} has a Condition, and conditions are stored as compiled delegates, not expressions, so a condition cannot be translated into a query expression. " +
                $"Express the rule inside a translatable MapFrom expression (for example a conditional expression) for projection, or map the values with the regular mapper instead.");
        if (pm.PreCondition != null)
            throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} has a PreCondition, and preconditions are stored as compiled delegates, not expressions, so a precondition cannot be translated into a query expression. " +
                $"Express the rule inside a translatable MapFrom expression for projection, or map the values with the regular mapper instead.");

        if (pm.Kind == PropertyMapKind.Custom)
        {
            if (pm.CustomExpression == null) throw new AutoMapperProjectionException(
                $"Destination member {pm.DestinationName} is a custom map with no source expression, which cannot be projected.");
            var raw = RebaseCustomWithParameters(sourceExpr, pm.CustomExpression, context);
            var converted = BuildValue(raw, pm.CustomExpression.ReturnType, pm.DestinationType, context);
            converted = ApplyNullSubstitute(raw, pm.CustomExpression.ReturnType, converted, pm, context);
            return EnsureType(converted, pm.DestinationType, pm.DestinationName);
        }

        if (pm.SourceMembers.Length == 0 && pm.SourceMemberPath.Length == 0)
        {
            if (pm.HasNullSubstitute)
                return EnsureType(Expression.Constant(pm.NullSubstitute, pm.DestinationType), pm.DestinationType, pm.DestinationName);
            // Unmapped member: Stage 1 leaves it alone, so projection
            // leaves it at its default rather than inventing a value.
            // BuildObject still needs a binding to keep MemberInit well
            // formed for value-type members, so return the default.
            // (For reference members the same default is null.)
            // Note: this path is only reached for members ResolveConventionalMaps
            // could not match; callers that validate will have caught it.
            return Expression.Default(pm.DestinationType);
        }

        var members = pm.SourceMembers.Length > 0 ? pm.SourceMembers : ResolveMembers(sourceType, pm.SourceMemberPath);
        var chain = BuildChain(sourceExpr, members);
        var leafType = MemberMatcher.MemberType(members[^1]);
        var safeChain = NullSafeRewriter.Rewrite(chain);
        var value = BuildValue(safeChain, leafType, pm.DestinationType, context);
        value = ApplyNullSubstitute(safeChain, leafType, value, pm, context);
        return EnsureType(value, pm.DestinationType, pm.DestinationName);
    }

    private static Expression ApplyNullSubstitute(Expression raw, Type rawType, Expression converted, PropertyMap pm, ProjectionBuildContext context)
    {
        if (!pm.HasNullSubstitute || !NullSafeRewriter.CanBeNull(rawType)) return converted;
        var substitute = pm.NullSubstitute == null
            ? Expression.Constant(null, pm.DestinationType)
            : EnsureType(Expression.Constant(pm.NullSubstitute, pm.NullSubstitute.GetType()), pm.DestinationType, pm.DestinationName);
        return Expression.Condition(Expression.Equal(raw, Expression.Constant(null, rawType)), substitute, converted);
    }

    private static Expression BuildCustomValue(Expression sourceExpr, LambdaExpression custom, Type destinationType, ProjectionBuildContext context)
    {
        var raw = RebaseCustomWithParameters(sourceExpr, custom, context);
        var converted = BuildValue(raw, custom.ReturnType, destinationType, context);
        return EnsureType(converted, destinationType, "constructor parameter");
    }

    private static Expression RebaseCustom(Expression sourceExpr, LambdaExpression custom)
    {
        if (custom.Parameters.Count == 2)
            throw new AutoMapperProjectionException(
                "A two-parameter MapFrom expression (source and destination) cannot be translated into a query expression, because no destination instance exists yet during projection. Use a single-parameter MapFrom expression for projection.");
        if (custom.Parameters.Count != 1)
            throw new AutoMapperProjectionException("A MapFrom expression for projection must take exactly one source parameter.");
        var param = custom.Parameters[0];
        Expression replacement = sourceExpr.Type == param.Type
            ? sourceExpr
            : param.Type.IsAssignableFrom(sourceExpr.Type)
                ? Expression.Convert(sourceExpr, param.Type)
                : throw new AutoMapperProjectionException(
                    $"A MapFrom expression over {param.Type.Name} cannot be projected from a {sourceExpr.Type.Name} source.");
        var rebased = new ProjectionVisitors.ParameterReplacer(param, replacement).Replace(custom.Body);
        return rebased;
    }

    // Rebase + parameterize in one pass for custom expressions: the
    // parameterizer needs the rebased tree, so parameter substitution
    // happens after rebasing (order matters only for closure values that
    // mention the source parameter, which cannot happen).
    private static Expression RebaseCustomWithParameters(Expression sourceExpr, LambdaExpression custom, ProjectionBuildContext context)
    {
        var rebased = RebaseCustom(sourceExpr, custom);
        if (context.Parameters != null && context.Parameters.Count > 0)
            rebased = new ProjectionVisitors.ClosureParameterizer(context.Parameters).Parameterize(rebased);
        return rebased;
    }

    private static Expression BuildChain(Expression sourceExpr, MemberInfo[] members)
    {
        var current = sourceExpr;
        foreach (var member in members) current = Expression.MakeMemberAccess(current, member);
        return current;
    }

    private static MemberInfo[] ResolveMembers(Type sourceType, string[] path)
    {
        var members = new List<MemberInfo>();
        var current = sourceType;
        foreach (var name in path)
        {
            var member = MemberMatcher.ReadableMembers(current)
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new AutoMapperMappingException($"Source member {name} on {current.Name} could not be resolved for projection.");
            members.Add(member);
            current = MemberMatcher.MemberType(member);
        }
        return members.ToArray();
    }

    private static Expression EnsureType(Expression value, Type destinationType, string memberName)
    {
        if (value.Type == destinationType) return value;
        if (destinationType.IsAssignableFrom(value.Type)) return Expression.Convert(value, destinationType);
        throw new AutoMapperProjectionException(
            $"The projected value for {memberName} is a {value.Type.Name}, which cannot be converted to {destinationType.Name} in a query expression.");
    }

    // ---- destination paths (ForPath) ------------------------------------------------

    private static List<MemberBinding> BuildPathBindings(Expression sourceExpr, Type sourceType, Type destinationType,
        TypeMap typeMap, ProjectionBuildContext context, HashSet<string> alreadyBound)
    {
        var result = new List<MemberBinding>();
        var live = typeMap.PathMaps.Where(p => !p.Ignored).ToList();
        if (live.Count == 0) return result;
        foreach (var group in live.GroupBy(p => p.DestinationPath[0], StringComparer.OrdinalIgnoreCase))
        {
            if (alreadyBound.Contains(group.Key))
                throw new AutoMapperProjectionException(
                    $"Destination path {string.Join(".", group.First().DestinationPath)} cannot be projected because its root member {group.Key} is also mapped as a property. Project the path and the property through a single member map instead.");
            var prop = destinationType.GetProperty(group.Key, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                throw new AutoMapperProjectionException($"Destination path root {group.Key} is not a writable property on {destinationType.Name}.");
            if (group.Count() == 1 && group.First().DestinationPath.Length == 1)
            {
                result.Add(Expression.Bind(prop, BuildPathLeaf(sourceExpr, group.First(), context)));
            }
            else
            {
                var subPaths = group.Select(p =>
                {
                    var sub = new PathMap(p.DestinationPath.Skip(1).ToArray(), p.DestinationType)
                    {
                        CustomExpression = p.CustomExpression,
                        Resolver = p.Resolver,
                        ResolverType = p.ResolverType,
                        MemberResolver = p.MemberResolver,
                        MemberResolverType = p.MemberResolverType,
                        TypeConverter = p.TypeConverter,
                        ConverterType = p.ConverterType,
                        Ignored = p.Ignored,
                        NullSubstitute = p.NullSubstitute,
                        HasNullSubstitute = p.HasNullSubstitute,
                        Condition = p.Condition,
                    };
                    sub.ValueTransformers.AddRange(p.ValueTransformers);
                    return sub;
                }).ToList();
                var nested = BuildNestedPathInit(sourceExpr, prop.PropertyType, subPaths, context);
                result.Add(Expression.Bind(prop, nested));
            }
        }
        return result;
    }

    private static Expression BuildNestedPathInit(Expression sourceExpr, Type intermediateType, List<PathMap> paths, ProjectionBuildContext context)
    {
        var ctor = intermediateType.GetConstructor(Type.EmptyTypes);
        if (ctor == null)
            throw new AutoMapperProjectionException($"Cannot project a destination path into {intermediateType.Name} because it has no parameterless constructor.");
        var bindings = new List<MemberBinding>();
        foreach (var group in paths.GroupBy(p => p.DestinationPath[0], StringComparer.OrdinalIgnoreCase))
        {
            var prop = intermediateType.GetProperty(group.Key, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                throw new AutoMapperProjectionException($"Destination path member {group.Key} is not a writable property on {intermediateType.Name}.");
            if (group.Count() == 1 && group.First().DestinationPath.Length == 1)
            {
                bindings.Add(Expression.Bind(prop, BuildPathLeaf(sourceExpr, group.First(), context)));
            }
            else
            {
                var sub = group.Select(p =>
                {
                    var nested = new PathMap(p.DestinationPath.Skip(1).ToArray(), p.DestinationType)
                    {
                        CustomExpression = p.CustomExpression,
                        Resolver = p.Resolver,
                        ResolverType = p.ResolverType,
                        MemberResolver = p.MemberResolver,
                        MemberResolverType = p.MemberResolverType,
                        TypeConverter = p.TypeConverter,
                        ConverterType = p.ConverterType,
                        NullSubstitute = p.NullSubstitute,
                        HasNullSubstitute = p.HasNullSubstitute,
                        Condition = p.Condition,
                    };
                    nested.ValueTransformers.AddRange(p.ValueTransformers);
                    return nested;
                }).ToList();
                bindings.Add(Expression.Bind(prop, BuildNestedPathInit(sourceExpr, prop.PropertyType, sub, context)));
            }
        }
        return bindings.Count == 0 ? (Expression)Expression.New(intermediateType) : Expression.MemberInit(Expression.New(intermediateType), bindings);
    }

    private static Expression BuildPathLeaf(Expression sourceExpr, PathMap path, ProjectionBuildContext context)
    {
        if (path.TypeConverter != null || path.ConverterType != null)
            throw new AutoMapperProjectionException($"Destination path {string.Join(".", path.DestinationPath)} uses a member converter, which cannot be translated into a query expression.");
        if (path.ResolverType != null || path.MemberResolver != null || path.MemberResolverType != null)
            throw new AutoMapperProjectionException($"Destination path {string.Join(".", path.DestinationPath)} uses a value resolver, which cannot be translated into a query expression.");
        if (path.ValueTransformers.Count > 0)
            throw new AutoMapperProjectionException($"Destination path {string.Join(".", path.DestinationPath)} uses value transformers, which cannot be translated into a query expression.");
        if (path.Condition != null)
            throw new AutoMapperProjectionException(
                $"Destination path {string.Join(".", path.DestinationPath)} has a Condition, and conditions are stored as compiled delegates, not expressions, so a condition cannot be translated into a query expression.");
        if (path.Resolver != null)
            throw new AutoMapperProjectionException($"Destination path {string.Join(".", path.DestinationPath)} uses a value resolver, which cannot be translated into a query expression.");
        if (path.CustomExpression == null)
        {
            if (path.HasNullSubstitute) return Expression.Constant(path.NullSubstitute, path.DestinationType);
            return Expression.Default(path.DestinationType);
        }
        var raw = RebaseCustomWithParameters(sourceExpr, path.CustomExpression, context);
        var converted = BuildValue(raw, path.CustomExpression.ReturnType, path.DestinationType, context);
        if (path.HasNullSubstitute && NullSafeRewriter.CanBeNull(path.CustomExpression.ReturnType))
        {
            var substitute = path.NullSubstitute == null
                ? Expression.Constant(null, path.DestinationType)
                : EnsureType(Expression.Constant(path.NullSubstitute, path.NullSubstitute.GetType()), path.DestinationType, "path leaf");
            converted = Expression.Condition(Expression.Equal(raw, Expression.Constant(null, path.CustomExpression.ReturnType)), substitute, converted);
        }
        return EnsureType(converted, path.DestinationType, "path leaf");
    }

    // ---- value conversion -----------------------------------------------------------

    internal static Expression BuildValue(Expression sourceExpr, Type sourceType, Type destinationType, ProjectionBuildContext context)
    {
        if (sourceType == destinationType) return sourceExpr;
        var typeMap = context.Configuration.FindTypeMap(sourceType, destinationType);
        if (typeMap != null)
        {
            int cap = typeMap.MaxDepth ?? context.Configuration.MaxDepthDefault;
            if (cap <= 0) cap = DefaultRecursiveCap;
            int already = context.Stack.Count(p => p.Source == sourceType && p.Destination == destinationType);
            if (already >= cap) return Expression.Default(destinationType);
            var body = BuildDestination(sourceExpr, sourceType, destinationType, context, topLevel: false);
            if (!NullSafeRewriter.CanBeNull(sourceType) || !NullSafeRewriter.CanBeNull(destinationType)) return EnsureType(body, destinationType, "nested member");
            return Expression.Condition(Expression.Equal(sourceExpr, Expression.Constant(null, sourceType)),
                Expression.Constant(null, destinationType), EnsureType(body, destinationType, "nested member"));
        }
        if (ProjectionMappers.TryConvert(sourceExpr, sourceType, destinationType, context, out var converted))
            return EnsureType(converted!, destinationType, "member");
        throw new AutoMapperMappingException(
            $"No map is configured from {sourceType.Name} to {destinationType.Name} for projection of a member value. " +
            $"Create a map for this pair before calling ProjectTo, or map the values with the regular mapper instead.");
    }

    // ---- TypeMap preparation ----------------------------------------------------------

    private static void EnsurePropertyMaps(TypeMap typeMap, IConfigurationProvider configuration)
    {
        if (!typeMap.IsSealed && configuration is MapperConfiguration concrete)
        {
            typeMap.ResolveConventionalMaps(concrete);
            typeMap.IsSealed = true;
        }
    }
}
