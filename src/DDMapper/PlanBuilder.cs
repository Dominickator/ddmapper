using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper;

/// <summary>Compiled plan signature: source, existing destination (or null), context -&gt; destination.</summary>
public delegate object? MappingPlan(object? source, object? destination, ResolutionContext context);

/// <summary>Thread-safe cache of compiled plans, one per type pair per configuration.
/// The cache lives on the configuration instance: two configurations mapping the same
/// pair with different member options must not share a plan.</summary>
public static class PlanCache
{
    public static MappingPlan GetOrBuild(IConfigurationProvider configuration, TypeMap typeMap)
    {
        var key = (typeMap.SourceType, typeMap.DestinationType);
        if (configuration is MapperConfiguration concrete)
        {
            lock (concrete.PlanLock)
            {
                if (concrete.CompiledPlans.TryGetValue(key, out var existing)) return existing;
            }
            if (!typeMap.IsSealed)
            {
                typeMap.ResolveConventionalMaps(concrete);
                typeMap.IsSealed = true;
            }
            var plan = PlanBuilder.Build(configuration, typeMap);
            lock (concrete.PlanLock) { concrete.CompiledPlans[key] = plan; }
            return plan;
        }
        // Fallback for foreign IConfigurationProvider implementations: compile without caching.
        return PlanBuilder.Build(configuration, typeMap);
    }
}

/// <summary>Builds an expression-tree plan for a TypeMap and compiles it once.</summary>
public static class PlanBuilder
{
    public static MappingPlan Build(IConfigurationProvider configuration, TypeMap typeMap)
    {
        // Whole-type converter shortcut: the plan is just the converter.
        if (typeMap.TypeConverterFunc != null)
        {
            var func = typeMap.TypeConverterFunc;
            return (source, destination, ctx) => source == null ? null : func(source);
        }
        if (typeMap.TypeConverter == null && typeMap.TypeConverterType != null)
        {
            // A map built without the seal-time deferred resolution (for
            // example a foreign provider compiling directly) still routes
            // its converter type through the service constructor.
            typeMap.TypeConverter = configuration.ServiceConstructor(typeMap.TypeConverterType);
            typeMap.TypeConverterType = null;
        }
        if (typeMap.TypeConverter != null)
        {
            var converter = typeMap.TypeConverter;
            return (source, destination, ctx) => source == null ? null : ConverterInvoker.Invoke(converter, source, destination, ctx);
        }

        var srcParam = Expression.Parameter(typeof(object), "source");
        var dstParam = Expression.Parameter(typeof(object), "destination");
        var ctxParam = Expression.Parameter(typeof(ResolutionContext), "context");
        var mapConst = Expression.Constant(typeMap);
        var srcTyped = Expression.Variable(typeMap.SourceType, "src");
        var dstTyped = Expression.Variable(typeMap.DestinationType, "dst");
        var body = new List<Expression>
        {
            Expression.Assign(srcTyped, Expression.Convert(srcParam, typeMap.SourceType)),
            Expression.Assign(dstTyped, Expression.Convert(Expression.Call(
                typeof(PlanExecutor), nameof(PlanExecutor.ConstructDestination), null, mapConst, srcTyped, dstParam, ctxParam), typeMap.DestinationType)),
        };

        // Register the new destination before member mapping so cycles resolve to it.
        body.Add(Expression.Call(typeof(PlanExecutor), nameof(PlanExecutor.RegisterReference), null, mapConst, srcTyped, dstTyped, ctxParam));
        body.Add(Expression.Call(typeof(PlanExecutor), nameof(PlanExecutor.InvokeBefore), null, mapConst, srcTyped, dstTyped, ctxParam));

        var ctorParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ctor = ChooseConstructor(typeMap);
        if (ctor != null)
            foreach (var p in ctor.GetParameters()) ctorParamNames.Add(p.Name ?? string.Empty);

        foreach (var propertyMap in typeMap.PropertyMaps)
        {
            if (propertyMap.Ignored) continue;
            if (ctor != null && ctor.GetParameters().Length > 0 && ctorParamNames.Contains(propertyMap.DestinationName)) continue;
            // An unmapped member (no source chain, no explicit value config) must be left
            // alone: assigning it would reset values set by ConstructUsing or already
            // present on an existing destination back to the type default.
            // Stage 3: a resolver member or a MapAtRuntime member is still
            // assigned — the resolver supplies the value, and a MapAtRuntime
            // member may resolve against the runtime source type.
            if (propertyMap.Kind == PropertyMapKind.Auto && propertyMap.SourceMemberPath.Length == 0
                && !propertyMap.HasNullSubstitute && propertyMap.Condition == null && propertyMap.PreCondition == null
                && propertyMap.Resolver == null && !propertyMap.MapAtRuntime)
                continue;
            var valueCall = Expression.Call(typeof(PlanExecutor), nameof(PlanExecutor.MapMemberValue), null,
                Expression.Constant(propertyMap), srcTyped, dstTyped, ctxParam);
            var assign = Expression.Assign(
                Expression.Property(dstTyped, propertyMap.DestinationProperty),
                Expression.Convert(valueCall, propertyMap.DestinationType));
            // Skip assignment when the helper reports "do not assign" via a sentinel: instead the
            // helper returns the current value for UseDestinationValue / skipped nulls, so a plain
            // assign is always safe.
            body.Add(assign);
        }
        foreach (var pathMap in typeMap.PathMaps)
        {
            if (pathMap.Ignored) continue;
            body.Add(Expression.Call(typeof(PlanExecutor), nameof(PlanExecutor.ApplyPath), null,
                Expression.Constant(pathMap), srcTyped, dstTyped, ctxParam));
        }

        body.Add(Expression.Call(typeof(PlanExecutor), nameof(PlanExecutor.InvokeAfter), null, mapConst, srcTyped, dstTyped, ctxParam));
        body.Add(Expression.Convert(dstTyped, typeof(object)));
        var block = Expression.Block(new[] { srcTyped, dstTyped }, body);
        var lambda = Expression.Lambda<MappingPlan>(block, srcParam, dstParam, ctxParam);
        return lambda.Compile();
    }

    internal static ConstructorInfo? ChooseConstructor(TypeMap typeMap)
    {
        if (typeMap.ConstructorMap.Constructor != null) return typeMap.ConstructorMap.Constructor;
        var destType = typeMap.DestinationType;
        if (destType.IsInterface || destType.IsAbstract) return null;
        var parameterless = destType.GetConstructor(Type.EmptyTypes);
        if (parameterless != null) return parameterless;
        // Prefer the constructor whose parameters all match source members or explicit ctor maps.
        foreach (var ctor in destType.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 0) continue;
            bool ok = parameters.All(p =>
                typeMap.ConstructorMap.Parameters.Any(cp => string.Equals(cp.ParameterName, p.Name, StringComparison.OrdinalIgnoreCase)) ||
                MemberMatcher.ReadableMembers(typeMap.SourceType).Any(m => string.Equals(m.Name, p.Name, StringComparison.OrdinalIgnoreCase)));
            if (ok) return ctor;
        }
        return null;
    }
}

/// <summary>Runtime helpers invoked by compiled plans. Kept separate so the expression
/// tree stays readable: construction, hooks, member resolution, and path assignment.</summary>
public static class PlanExecutor
{
    public static object ConstructDestination(TypeMap typeMap, object source, object? existing, ResolutionContext ctx)
    {
        if (existing != null) return existing;
        if (typeMap.ConstructUsingWithContext != null) return typeMap.ConstructUsingWithContext(source, ctx);
        if (typeMap.ConstructUsingFunc != null) return typeMap.ConstructUsingFunc(source);
        var destType = typeMap.DestinationType;
        // --- STAGE 2 (proxy) BEGIN: interface and abstract destinations are
        // satisfied by a runtime proxy type instead of throwing. ConstructUsing
        // (checked above) still wins, and an existing destination is reused.
        // Unproxyable destinations still throw a clear AutoMapperMappingException
        // from ProxyGenerator (see its documented boundary). ---
        if (destType.IsInterface || destType.IsAbstract)
            return Execution.ProxyGenerator.CreateProxy(destType);
        // --- STAGE 2 (proxy) END ---
        var ctor = PlanBuilder.ChooseConstructor(typeMap);
        if (ctor == null)
            throw new AutoMapperMappingException(
                $"Cannot create a {destType.Name}: it has no parameterless constructor and no constructor whose parameters match the source members of {typeMap.SourceType.Name}. Provide a construction function for this map.");
        var parameters = ctor.GetParameters();
        if (parameters.Length == 0) return ctor.Invoke(null);
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var explicitMap = typeMap.ConstructorMap.Parameters.FirstOrDefault(p => string.Equals(p.ParameterName, param.Name, StringComparison.OrdinalIgnoreCase));
            if (explicitMap?.CustomExpression != null)
            {
                args[i] = explicitMap.CustomExpression.Compile().DynamicInvoke(source);
            }
            else
            {
                var member = MemberMatcher.ReadableMembers(typeMap.SourceType)
                    .FirstOrDefault(m => string.Equals(m.Name, param.Name, StringComparison.OrdinalIgnoreCase));
                if (member == null) { args[i] = param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null; continue; }
                var value = MemberMatcher.GetValue(member, source);
                args[i] = value == null ? null : MapperRuntime.MapValue(ctx, value, MemberMatcher.MemberType(member), param.ParameterType, typeMap);
            }
        }
        return ctor.Invoke(args);
    }

    public static void RegisterReference(TypeMap typeMap, object source, object destination, ResolutionContext ctx)
    {
        if (typeMap.PreserveReferences && source != null && destination != null)
            ctx.ReferenceCache[source] = destination;
    }

    public static void InvokeBefore(TypeMap typeMap, object source, object destination, ResolutionContext ctx)
    {
        // Stage 3: mapping features run outermost — before the map's own hooks.
        foreach (var feature in ctx.MappingFeatures.All) feature.BeforeMap(source, destination, ctx);
        foreach (var action in typeMap.BeforeMapActions) action(source, destination, ctx);
        if (ctx.Options != null)
        {
            foreach (var action in ctx.Options.BeforeMapActions) action(source, destination);
            if (ctx.Options is ITypedMappingActions typed) typed.InvokeTypedBefore(source, destination);
        }
    }

    public static void InvokeAfter(TypeMap typeMap, object source, object destination, ResolutionContext ctx)
    {
        foreach (var action in typeMap.AfterMapActions) action(source, destination, ctx);
        if (ctx.Options != null)
        {
            foreach (var action in ctx.Options.AfterMapActions) action(source, destination);
            if (ctx.Options is ITypedMappingActions typed) typed.InvokeTypedAfter(source, destination);
        }
        // Stage 3: mapping features run outermost — after the map's own hooks.
        foreach (var feature in ctx.MappingFeatures.All) feature.AfterMap(source, destination, ctx);
    }

    public static object? MapMemberValue(PropertyMap propertyMap, object source, object destination, ResolutionContext ctx)
    {
        var current = SafeGet(propertyMap, destination);
        if (propertyMap.UseDestinationValue) return current;
        if (propertyMap.PreCondition != null && !propertyMap.PreCondition(source)) return current;

        // Member-value resolver: receives the conventionally resolved
        // source member value and the current destination member value.
        // Its result is already in destination-member form, so it skips
        // the nested-mapping step but still passes through a member
        // converter (when one is also configured) and the transformers.
        if (propertyMap.MemberResolver != null)
        {
            // The source member value comes from a MapFrom expression
            // when one is also configured, else from the conventional
            // chain (MapAtRuntime re-resolved), else the default: a
            // member with no source chain has no source member value,
            // rather than the whole source object (which is what an
            // empty chain walk would return).
            object? sourceMember = propertyMap.CustomExpression != null
                ? propertyMap.ResolveCustom(source, destination, ctx)
                : propertyMap.MapAtRuntime
                    ? WalkChainAtRuntime(source, propertyMap) ?? WalkChain(source, propertyMap.SourceMembers)
                    : propertyMap.SourceMembers.Length > 0
                        ? WalkChain(source, propertyMap.SourceMembers)
                        : null;
            var resolved = InvokeMemberResolver(propertyMap.MemberResolver, source, destination, sourceMember, current, ctx);
            if (propertyMap.Condition != null && !propertyMap.Condition(source, destination, resolved, ctx)) return current;
            if (resolved == null)
            {
                if (propertyMap.HasNullSubstitute) return ApplyTransformers(propertyMap, ctx, propertyMap.NullSubstitute);
                if (!ctx.Configuration.AllowNullDestinationValues) return current;
                return propertyMap.DestinationType.IsValueType && Nullable.GetUnderlyingType(propertyMap.DestinationType) == null
                    ? Activator.CreateInstance(propertyMap.DestinationType)
                    : null;
            }
            if (propertyMap.TypeConverter != null)
                return ApplyTransformers(propertyMap, ctx, ConverterInvoker.Invoke(propertyMap.TypeConverter, resolved, current, ctx));
            return ApplyTransformers(propertyMap, ctx, resolved);
        }

        object? raw;
        if (propertyMap.Resolver != null || propertyMap.CustomExpression != null)
        {
            raw = propertyMap.ResolveCustom(source, destination, ctx);
        }
        else if (propertyMap.MapAtRuntime)
        {
            // Stage 3: re-resolve the chain by name against the runtime source
            // type, so a derived runtime source's hiding member wins over the
            // plan-time (declared-type) member. Falls back to the plan-time
            // chain when the runtime lookup fails at any step.
            raw = WalkChainAtRuntime(source, propertyMap) ?? WalkChain(source, propertyMap.SourceMembers);
        }
        else if (propertyMap.SourceMembers.Length > 0)
        {
            raw = WalkChain(source, propertyMap.SourceMembers);
        }
        else
        {
            raw = null;
        }

        if (propertyMap.Condition != null && !propertyMap.Condition(source, destination, raw, ctx)) return current;
        if (raw == null)
        {
            if (propertyMap.HasNullSubstitute) return ApplyTransformers(propertyMap, ctx, propertyMap.NullSubstitute);
            if (!ctx.Configuration.AllowNullDestinationValues) return current;
            // Null collections become empty collections when that flag is off.
            if (!ctx.Configuration.AllowNullCollections && MapperRuntime.IsCollectionType(propertyMap.DestinationType) && !MapperRuntime.IsDictionaryType(propertyMap.DestinationType))
            {
                var elementType = MapperRuntime.ElementTypeOf(propertyMap.DestinationType) ?? typeof(object);
                return FallbackChain.Materialize(new List<object?>(), propertyMap.DestinationType, elementType);
            }
            return propertyMap.DestinationType.IsValueType && Nullable.GetUnderlyingType(propertyMap.DestinationType) == null
                ? Activator.CreateInstance(propertyMap.DestinationType)
                : null;
        }

        if (propertyMap.TypeConverter != null)
        {
            return ApplyTransformers(propertyMap, ctx, ConverterInvoker.Invoke(propertyMap.TypeConverter, raw, current, ctx));
        }

        var sourceType = raw.GetType();
        // Nested dispatch mirrors the top level: the runtime map wins,
        // but when none exists the declared member type's map applies.
        // That is what lets a MapAtRuntime member on a nested map see
        // a derived runtime source (the declared map's plan re-resolves
        // the chain against the runtime type) instead of failing to
        // find any map for the derived type.
        var declaredType = propertyMap.SourceMembers.Length > 0
            ? MemberMatcher.MemberType(propertyMap.SourceMembers[^1])
            : propertyMap.CustomExpression != null
                ? propertyMap.CustomExpression.ReturnType
                : sourceType;
        var nested = ((Mapper)ctx.Mapper).MapInternal(raw, sourceType, propertyMap.DestinationType, null, ctx, declaredType);
        return ApplyTransformers(propertyMap, ctx, nested);
    }

    /// <summary>Applies value transformers to a final member value, in
    /// order: global transformers (registration order, only those whose
    /// member type covers this destination type), then map-level, then
    /// per-member. Kept destination values (UseDestinationValue,
    /// failed preconditions/conditions, skipped nulls) are not
    /// transformed; only values this map actually produces are.</summary>
    internal static object? ApplyTransformers(PropertyMap propertyMap, ResolutionContext ctx, object? value)
    {
        if (value == null) return null;
        var result = value;
        foreach (var registration in ctx.Configuration.GlobalValueTransformers)
        {
            if (registration.AppliesTo(propertyMap.DestinationType))
                result = registration.Transform(result);
        }
        var typeMap = ctx.TypeMap;
        if (typeMap != null)
            foreach (var registration in typeMap.ValueTransformers)
                if (registration.AppliesTo(propertyMap.DestinationType))
                    result = registration.Transform(result);
        foreach (var transform in propertyMap.ValueTransformers)
            result = transform(result);
        return result;
    }

    private static object? InvokeMemberResolver(object resolver, object source, object destination, object? sourceMember, object? destMember, ResolutionContext ctx)
    {
        var method = resolver.GetType().GetMethod("Resolve")!;
        return method.Invoke(resolver, new object?[] { source, destination, sourceMember, destMember, ctx });
    }

    public static void ApplyPath(PathMap pathMap, object source, object destination, ResolutionContext ctx)
    {
        object? raw = null;
        if (pathMap.MemberResolver != null)
        {
            raw = InvokeMemberResolver(pathMap.MemberResolver, source, destination, null, null, ctx);
        }
        else if (pathMap.Resolver != null)
        {
            var method = pathMap.Resolver.GetType().GetMethod("Resolve")!;
            raw = method.Invoke(pathMap.Resolver, new object?[] { source, destination, null, ctx });
        }
        else if (pathMap.CustomExpression != null)
        {
            var compiled = pathMap.CustomExpression.Compile();
            raw = pathMap.CustomExpression.Parameters.Count == 2
                ? compiled.DynamicInvoke(source, destination)
                : compiled.DynamicInvoke(source);
        }
        if (pathMap.Condition != null && !pathMap.Condition(source, destination, raw, ctx)) return;
        if (raw == null)
        {
            if (pathMap.HasNullSubstitute) raw = pathMap.NullSubstitute;
            else return;
        }
        if (pathMap.TypeConverter != null)
            raw = ConverterInvoker.Invoke(pathMap.TypeConverter, raw, null, ctx);
        var mapped = raw == null ? null : MapperRuntime.MapValue(ctx, raw, raw.GetType(), pathMap.DestinationType, null);
        if (mapped != null)
        {
            foreach (var transform in pathMap.ValueTransformers) mapped = transform(mapped);
        }
        // Walk/create intermediate objects along the destination path.
        object current = destination;
        for (int i = 0; i < pathMap.DestinationPath.Length - 1; i++)
        {
            var prop = current.GetType().GetProperty(pathMap.DestinationPath[i]);
            if (prop == null) return;
            var next = prop.GetValue(current);
            if (next == null)
            {
                try { next = Activator.CreateInstance(prop.PropertyType); }
                catch { return; }
                if (prop.CanWrite) prop.SetValue(current, next);
                else return;
            }
            current = next!;
        }
        var leaf = current.GetType().GetProperty(pathMap.DestinationPath[^1]);
        if (leaf != null && leaf.CanWrite) leaf.SetValue(current, mapped);
    }

    private static object? WalkChain(object source, MemberInfo[] chain)
    {
        object? current = source;
        foreach (var member in chain)
        {
            if (current == null) return null;
            current = MemberMatcher.GetValue(member, current);
        }
        return current;
    }

    /// <summary>Walks a member chain by name against each step's runtime type.
    /// Returns null when any step cannot be found by name (the caller then
    /// falls back to the plan-time chain) — except when the chain itself
    /// legitimately resolves to a null value, which is indistinguishable here
    /// and equally correct as a fallback result.</summary>
    private static object? WalkChainAtRuntime(object source, PropertyMap propertyMap)
    {
        var names = propertyMap.SourceMemberPath.Length > 0
            ? propertyMap.SourceMemberPath
            : new[] { propertyMap.DestinationName };
        object? current = source;
        foreach (var name in names)
        {
            if (current == null) return null;
            // Walk the runtime type hierarchy most-derived first, so a
            // hiding (`new`) member on a derived runtime type wins over the
            // base member that reflection enumeration might return first.
            MemberInfo? member = null;
            for (var t = current.GetType(); t != null && member == null; t = t.BaseType)
            {
                member = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.CanRead) as MemberInfo
                    ?? t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                        .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            if (member == null) return null;
            current = MemberMatcher.GetValue(member, current);
        }
        return current;
    }

    private static object? SafeGet(PropertyMap map, object destination)
    {
        try { return map.DestinationProperty.GetValue(destination); } catch { return null; }
    }
}
