using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper;

/// <summary>Base class for profiles: group related maps in one place.
/// Maps may be declared in the constructor (recorded and replayed on AddProfile)
/// or by overriding <see cref="Configure"/>.
/// </summary>
public abstract class Profile
{
    protected Profile() { }
    protected Profile(string profileName) { ProfileName = profileName; }
    public string ProfileName { get; } = string.Empty;
    internal MapperConfigurationExpression? Expression { get; set; }
    internal List<Action<IMapperConfigurationExpression>> PendingMaps { get; } = new();

    /// <summary>Override to declare maps after the profile is attached to a configuration.</summary>
    protected virtual void Configure() { }

    internal void Attach(MapperConfigurationExpression expression)
    {
        Expression = expression;
        foreach (var pending in PendingMaps) pending(expression);
        PendingMaps.Clear();
        Configure();
    }

    protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        if (Expression != null) return Expression.CreateMap<TSource, TDestination>();
        var deferred = new DeferredMappingExpression<TSource, TDestination>();
        PendingMaps.Add(cfg =>
        {
            var real = cfg.CreateMap<TSource, TDestination>();
            foreach (var step in deferred.Steps) step(real);
        });
        return deferred;
    }
    protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(MemberList memberList)
    {
        if (Expression != null) return Expression.CreateMap<TSource, TDestination>(memberList);
        var deferred = new DeferredMappingExpression<TSource, TDestination>();
        PendingMaps.Add(cfg =>
        {
            var real = cfg.CreateMap<TSource, TDestination>(memberList);
            foreach (var step in deferred.Steps) step(real);
        });
        return deferred;
    }
}

/// <summary>Fluent per-member configuration, backed by a PropertyMap or PathMap.</summary>
public sealed class MemberConfigurationExpression<TSource, TDestination, TMember>
    : IMemberConfigurationExpression<TSource, TDestination, TMember>
{
    private readonly PropertyMap? _propertyMap;
    private readonly PathMap? _pathMap;

    internal MemberConfigurationExpression(PropertyMap map) { _propertyMap = map; }
    internal MemberConfigurationExpression(PathMap map) { _pathMap = map; }

    public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceMember) => SetCustom(sourceMember);
    public void MapFrom(Expression<Func<TSource, TDestination, TMember>> sourceMember) => SetCustom(sourceMember);
    public void MapFrom<TResolver>(TResolver resolver) where TResolver : IValueResolver<TSource, TDestination, TMember>
    {
        if (_propertyMap != null) { _propertyMap.Resolver = resolver; _propertyMap.Kind = PropertyMapKind.Custom; }
        else if (_pathMap != null) _pathMap.Resolver = resolver;
    }
    public void MapFrom<TResolver>() where TResolver : class, IValueResolver<TSource, TDestination, TMember>
    {
        if (_propertyMap != null) { _propertyMap.ResolverType = typeof(TResolver); _propertyMap.Kind = PropertyMapKind.Custom; }
        else if (_pathMap != null) _pathMap.ResolverType = typeof(TResolver);
    }
    public void MapFrom<TResolver, TSourceMember>(TResolver resolver) where TResolver : IMemberValueResolver<TSource, TDestination, TSourceMember, TMember>
    {
        if (_propertyMap != null) { _propertyMap.MemberResolver = resolver; _propertyMap.Kind = PropertyMapKind.Custom; }
        else if (_pathMap != null) _pathMap.MemberResolver = resolver;
    }
    public void MapFromMember<TResolver, TSourceMember>() where TResolver : class, IMemberValueResolver<TSource, TDestination, TSourceMember, TMember>
    {
        if (_propertyMap != null) { _propertyMap.MemberResolverType = typeof(TResolver); _propertyMap.Kind = PropertyMapKind.Custom; }
        else if (_pathMap != null) _pathMap.MemberResolverType = typeof(TResolver);
    }
    public void Ignore()
    {
        if (_propertyMap != null) _propertyMap.Kind = PropertyMapKind.Ignored;
        else if (_pathMap != null) _pathMap.Ignored = true;
    }
    public void Condition(Func<TSource, bool> condition) => SetCondition((s, d, m, ctx) => condition((TSource)s));
    public void Condition(Func<TSource, TDestination, bool> condition) => SetCondition((s, d, m, ctx) => condition((TSource)s, (TDestination)d));
    public void Condition(Func<TSource, TDestination, TMember, bool> condition) =>
        SetCondition((s, d, m, ctx) => condition((TSource)s, (TDestination)d, m is TMember typed ? typed : default!));
    public void PreCondition(Func<TSource, bool> condition)
    {
        if (_propertyMap != null) _propertyMap.PreCondition = s => condition((TSource)s);
    }
    public void NullSubstitute(TMember? substitute)
    {
        if (_propertyMap != null) { _propertyMap.NullSubstitute = substitute; _propertyMap.HasNullSubstitute = true; }
        else if (_pathMap != null) { _pathMap.NullSubstitute = substitute; _pathMap.HasNullSubstitute = true; }
    }
    public void UseDestinationValue()
    {
        if (_propertyMap != null) _propertyMap.UseDestinationValue = true;
    }
    public void ConvertUsing(ITypeConverter<object, TMember> converter)
    {
        if (_propertyMap != null) _propertyMap.TypeConverter = converter;
        else if (_pathMap != null) _pathMap.TypeConverter = converter;
    }
    public void ConvertUsing<TSourceMember>(IValueConverter<TSourceMember, TMember> converter)
    {
        if (_propertyMap != null) _propertyMap.TypeConverter = converter;
        else if (_pathMap != null) _pathMap.TypeConverter = converter;
    }
    public void ConvertUsing<TConverter>() where TConverter : class
    {
        if (_propertyMap != null) _propertyMap.ConverterType = typeof(TConverter);
        else if (_pathMap != null) _pathMap.ConverterType = typeof(TConverter);
    }
    public void AddValueTransformer(Func<TMember, TMember> transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        if (_propertyMap != null) _propertyMap.ValueTransformers.Add(v => transformer(v is TMember typed ? typed : default!));
        else if (_pathMap != null) _pathMap.ValueTransformers.Add(v => transformer(v is TMember typed ? typed : default!));
    }
    public void MapAtRuntime()
    {
        if (_propertyMap != null) _propertyMap.MapAtRuntime = true;
    }

    private void SetCustom(LambdaExpression expr)
    {
        if (_propertyMap != null) { _propertyMap.CustomExpression = expr; _propertyMap.Kind = PropertyMapKind.Custom; }
        else if (_pathMap != null) _pathMap.CustomExpression = expr;
    }
    private void SetCondition(Func<object, object, object?, ResolutionContext, bool> condition)
    {
        if (_propertyMap != null) _propertyMap.Condition = condition;
        else if (_pathMap != null) _pathMap.Condition = condition;
    }
}

/// <summary>Per-constructor-parameter configuration.</summary>
public sealed class CtorParamConfigurationExpression<TSource> : ICtorParamConfigurationExpression<TSource>
{
    private readonly ConstructorParameterMap _map;
    internal CtorParamConfigurationExpression(ConstructorParameterMap map) { _map = map; }
    public void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember) => _map.CustomExpression = sourceMember;
}

/// <summary>Per-source-member configuration.</summary>
public sealed class SourceMemberConfigurationExpression : ISourceMemberConfigurationExpression
{
    private readonly TypeMap _typeMap;
    private readonly string _name;
    internal SourceMemberConfigurationExpression(TypeMap typeMap, string name) { _typeMap = typeMap; _name = name; }
    public void Ignore() => _typeMap.IgnoredSourceMembers.Add(_name);
    public void DoNotValidate() => _typeMap.UnvalidatedSourceMembers.Add(_name);
}

/// <summary>Fluent configuration for one source/destination pair.</summary>
public sealed class MappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
{
    internal MappingExpression(TypeMap typeMap, MapperConfigurationExpression configuration)
    {
        TypeMap = typeMap;
        Configuration = configuration;
    }
    internal TypeMap TypeMap { get; }
    internal MapperConfigurationExpression Configuration { get; }

    private void GuardSealed()
    {
        if (TypeMap.IsSealed)
            throw new AutoMapperConfigurationException(
                $"The map from {typeof(TSource).Name} to {typeof(TDestination).Name} can no longer be changed because its configuration has already been built.");
    }

    public IMappingExpression<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> memberOptions)
    {
        GuardSealed();
        var property = ExtractProperty(destinationMember);
        var map = TypeMap.GetOrCreatePropertyMap(property);
        memberOptions(new MemberConfigurationExpression<TSource, TDestination, TMember>(map));
        return this;
    }

    public IMappingExpression<TSource, TDestination> ForPath<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> memberOptions)
    {
        GuardSealed();
        var path = ExtractPath(destinationMember);
        var destType = typeof(TMember);
        var pathMap = new PathMap(path, destType);
        TypeMap.PathMaps.Add(pathMap);
        memberOptions(new MemberConfigurationExpression<TSource, TDestination, TMember>(pathMap));
        return this;
    }

    public IMappingExpression<TSource, TDestination> ForSourceMember<TMember>(
        Expression<Func<TSource, TMember>> sourceMember,
        Action<ISourceMemberConfigurationExpression> memberOptions)
    {
        GuardSealed();
        var name = ExtractName(sourceMember);
        memberOptions(new SourceMemberConfigurationExpression(TypeMap, name));
        return this;
    }

    public IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter)
    {
        GuardSealed();
        TypeMap.TypeConverterFunc = s => converter((TSource)s)!;
        return this;
    }

    public IMappingExpression<TSource, TDestination> ConvertUsing(ITypeConverter<TSource, TDestination> converter)
    {
        GuardSealed();
        TypeMap.TypeConverter = converter;
        return this;
    }

    public IMappingExpression<TSource, TDestination> ConvertUsing<TConverter>() where TConverter : class
    {
        GuardSealed();
        TypeMap.TypeConverterType = typeof(TConverter);
        return this;
    }

    public IMappingExpression<TSource, TDestination> BeforeMap<TAction>() where TAction : class, IMappingAction<TSource, TDestination>
    {
        GuardSealed();
        TypeMap.BeforeMapActionTypes.Add(typeof(TAction));
        return this;
    }
    public IMappingExpression<TSource, TDestination> BeforeMap(IMappingAction<TSource, TDestination> action)
    {
        GuardSealed();
        ArgumentNullException.ThrowIfNull(action);
        TypeMap.BeforeMapActions.Add((s, d, ctx) => action.Process((TSource)s, (TDestination)d, ctx));
        return this;
    }
    public IMappingExpression<TSource, TDestination> AfterMap<TAction>() where TAction : class, IMappingAction<TSource, TDestination>
    {
        GuardSealed();
        TypeMap.AfterMapActionTypes.Add(typeof(TAction));
        return this;
    }
    public IMappingExpression<TSource, TDestination> AfterMap(IMappingAction<TSource, TDestination> action)
    {
        GuardSealed();
        ArgumentNullException.ThrowIfNull(action);
        TypeMap.AfterMapActions.Add((s, d, ctx) => action.Process((TSource)s, (TDestination)d, ctx));
        return this;
    }

    public IMappingExpression<TSource, TDestination> AddValueTransformer<TMember>(Func<TMember, TMember> transformer)
    {
        GuardSealed();
        ArgumentNullException.ThrowIfNull(transformer);
        TypeMap.ValueTransformers.Add(new ValueTransformerRegistration(typeof(TMember), v => transformer(v is TMember typed ? typed : default!)));
        return this;
    }

    public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> constructor)
    {
        GuardSealed();
        TypeMap.ConstructUsingFunc = s => constructor((TSource)s)!;
        return this;
    }

    public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, ResolutionContext, TDestination> constructor)
    {
        GuardSealed();
        TypeMap.ConstructUsingWithContext = (s, ctx) => constructor((TSource)s, ctx)!;
        return this;
    }

    public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action)
    {
        GuardSealed();
        TypeMap.BeforeMapActions.Add((s, d, ctx) => action((TSource)s, (TDestination)d));
        return this;
    }
    public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination, ResolutionContext> action)
    {
        GuardSealed();
        TypeMap.BeforeMapActions.Add((s, d, ctx) => action((TSource)s, (TDestination)d, ctx));
        return this;
    }
    public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action)
    {
        GuardSealed();
        TypeMap.AfterMapActions.Add((s, d, ctx) => action((TSource)s, (TDestination)d));
        return this;
    }
    public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination, ResolutionContext> action)
    {
        GuardSealed();
        TypeMap.AfterMapActions.Add((s, d, ctx) => action((TSource)s, (TDestination)d, ctx));
        return this;
    }

    public IMappingExpression<TSource, TDestination> IncludeBase<TBaseSource, TBaseDestination>()
    {
        GuardSealed();
        TypeMap.IncludedBase = (typeof(TBaseSource), typeof(TBaseDestination));
        // Inherit the base pair's property maps so base members map without re-declaring them.
        var baseMap = Configuration.FindTypeMapInternal(typeof(TBaseSource), typeof(TBaseDestination));
        if (baseMap != null)
            foreach (var pm in baseMap.PropertyMaps)
            {
                var destProp = typeof(TDestination).GetProperty(pm.DestinationName);
                if (destProp != null && TypeMap.FindPropertyMap(destProp.Name) == null)
                {
                    var copy = TypeMap.GetOrCreatePropertyMap(destProp);
                    copy.Kind = pm.Kind;
                    copy.CustomExpression = pm.CustomExpression;
                    copy.Resolver = pm.Resolver;
                    copy.SourceMemberPath = pm.SourceMemberPath;
                    copy.SourceMembers = pm.SourceMembers;
                    copy.Condition = pm.Condition;
                    copy.PreCondition = pm.PreCondition;
                    copy.NullSubstitute = pm.NullSubstitute;
                    copy.HasNullSubstitute = pm.HasNullSubstitute;
                    // Stage 3: carry the remaining member options across too.
                    copy.UseDestinationValue = pm.UseDestinationValue;
                    copy.MapAtRuntime = pm.MapAtRuntime;
                    copy.TypeConverter = pm.TypeConverter;
                    copy.ConverterType = pm.ConverterType;
                    copy.ResolverType = pm.ResolverType;
                    copy.MemberResolver = pm.MemberResolver;
                    copy.MemberResolverType = pm.MemberResolverType;
                    copy.ValueTransformers.AddRange(pm.ValueTransformers);
                    copy.MappingOrder = pm.MappingOrder;
                    copy.HasMappingOrder = pm.HasMappingOrder;
                }
            }
        return this;
    }

    public IMappingExpression<TSource, TDestination> Include<TDerivedSource, TDerivedDestination>()
    {
        GuardSealed();
        TypeMap.IncludedDerived.Add((typeof(TDerivedSource), typeof(TDerivedDestination)));
        return this;
    }

    public IMappingExpression<TSource, TDestination> MaxDepth(int depth)
    {
        GuardSealed();
        TypeMap.MaxDepth = depth;
        return this;
    }

    public IMappingExpression<TSource, TDestination> PreserveReferences()
    {
        GuardSealed();
        TypeMap.PreserveReferences = true;
        return this;
    }

    public IMappingExpression<TDestination, TSource> ReverseMap()
    {
        GuardSealed();
        var reverse = Configuration.CreateMap<TDestination, TSource>();
        // Mirror explicit custom member maps back where the source expression is a simple member access.
        foreach (var pm in TypeMap.PropertyMaps.Where(p => p.Kind == PropertyMapKind.Custom && p.CustomExpression != null))
        {
            var srcName = ExtractNameOrNull(pm.CustomExpression!);
            if (srcName == null) continue;
            var reverseProp = typeof(TSource).GetProperty(srcName);
            var destProp = typeof(TDestination).GetProperty(pm.DestinationName);
            if (reverseProp == null || destProp == null) continue;
            // Only mirror when the reverse destination member exists as a writable property.
            var reverseMap = ((MappingExpression<TDestination, TSource>)reverse).TypeMap;
            var rpm = reverseMap.GetOrCreatePropertyMap(reverseProp);
            rpm.Kind = PropertyMapKind.Custom;
            var srcParam = Expression.Parameter(typeof(TDestination), "src");
            rpm.CustomExpression = Expression.Lambda(Expression.Property(srcParam, destProp), srcParam);
        }
        return reverse;
    }

    public IMappingExpression<TSource, TDestination> ForCtorParam(string paramName, Action<ICtorParamConfigurationExpression<TSource>> options)
    {
        GuardSealed();
        var existing = TypeMap.ConstructorMap.Parameters.FirstOrDefault(p => p.ParameterName == paramName);
        if (existing == null)
        {
            existing = new ConstructorParameterMap(paramName, typeof(object));
            TypeMap.ConstructorMap.Parameters.Add(existing);
        }
        options(new CtorParamConfigurationExpression<TSource>(existing));
        return this;
    }

    public IMappingExpression<TSource, TDestination> DisableCtorValidation()
    {
        GuardSealed();
        TypeMap.ConstructorMap.DisableValidation = true;
        return this;
    }

    internal static PropertyInfo ExtractProperty<TMember>(Expression<Func<TDestination, TMember>> expr)
    {
        var body = expr.Body is UnaryExpression unary ? unary.Operand : expr.Body;
        if (body is MemberExpression member && member.Member is PropertyInfo property) return property;
        throw new AutoMapperConfigurationException("ForMember expects a simple destination property access such as dest => dest.Name.");
    }

    internal static string[] ExtractPath<TMember>(Expression<Func<TDestination, TMember>> expr)
    {
        var parts = new List<string>();
        var node = expr.Body is UnaryExpression unary ? unary.Operand : expr.Body;
        while (node is MemberExpression member)
        {
            parts.Add(member.Member.Name);
            node = member.Expression!;
        }
        parts.Reverse();
        if (parts.Count == 0)
            throw new AutoMapperConfigurationException("ForPath expects a destination member path such as dest => dest.Address.City.");
        return parts.ToArray();
    }

    internal static string ExtractName<TMember>(Expression<Func<TSource, TMember>> expr)
    {
        var body = expr.Body is UnaryExpression unary ? unary.Operand : expr.Body;
        if (body is MemberExpression member) return member.Member.Name;
        throw new AutoMapperConfigurationException("ForSourceMember expects a simple source member access such as src => src.Name.");
    }

    private static string? ExtractNameOrNull(LambdaExpression expr)
    {
        var body = expr.Body is UnaryExpression unary ? unary.Operand : expr.Body;
        return body is MemberExpression member ? member.Member.Name : null;
    }
}
