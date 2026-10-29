using System.Linq.Expressions;

namespace AutoMapper;

/// <summary>
/// Deferred mapping expression used when a Profile configures maps inside its constructor,
/// before the configuration object exists. Each chained call is recorded and replayed
/// against the real configuration when the profile is added.
/// </summary>
internal sealed class DeferredMappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
{
    internal List<Action<IMappingExpression<TSource, TDestination>>> Steps { get; } = new();

    private DeferredMappingExpression<TSource, TDestination> Record(Action<IMappingExpression<TSource, TDestination>> step)
    {
        Steps.Add(step);
        return this;
    }

    public IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> d, Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> o)
        => Record(m => m.ForMember(d, o));
    public IMappingExpression<TSource, TDestination> ForPath<TMember>(Expression<Func<TDestination, TMember>> d, Action<IMemberConfigurationExpression<TSource, TDestination, TMember>> o)
        => Record(m => m.ForPath(d, o));
    public IMappingExpression<TSource, TDestination> ForSourceMember<TMember>(Expression<Func<TSource, TMember>> s, Action<ISourceMemberConfigurationExpression> o)
        => Record(m => m.ForSourceMember(s, o));
    public IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> c) => Record(m => m.ConvertUsing(c));
    public IMappingExpression<TSource, TDestination> ConvertUsing(ITypeConverter<TSource, TDestination> c) => Record(m => m.ConvertUsing(c));
    public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> c) => Record(m => m.ConstructUsing(c));
    public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, ResolutionContext, TDestination> c) => Record(m => m.ConstructUsing(c));
    public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> a) => Record(m => m.BeforeMap(a));
    public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination, ResolutionContext> a) => Record(m => m.BeforeMap(a));
    public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> a) => Record(m => m.AfterMap(a));
    public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination, ResolutionContext> a) => Record(m => m.AfterMap(a));
    public IMappingExpression<TSource, TDestination> IncludeBase<TBaseSource, TBaseDestination>() => Record(m => m.IncludeBase<TBaseSource, TBaseDestination>());
    public IMappingExpression<TSource, TDestination> Include<TDerivedSource, TDerivedDestination>() => Record(m => m.Include<TDerivedSource, TDerivedDestination>());
    public IMappingExpression<TSource, TDestination> MaxDepth(int depth) => Record(m => m.MaxDepth(depth));
    public IMappingExpression<TSource, TDestination> PreserveReferences() => Record(m => m.PreserveReferences());
    public IMappingExpression<TDestination, TSource> ReverseMap() => throw new AutoMapperConfigurationException(
        "ReverseMap cannot be recorded from a profile constructor; call ReverseMap inside Configure() or after AddProfile instead.");
    public IMappingExpression<TSource, TDestination> ForCtorParam(string name, Action<ICtorParamConfigurationExpression<TSource>> o) => Record(m => m.ForCtorParam(name, o));
    public IMappingExpression<TSource, TDestination> DisableCtorValidation() => Record(m => m.DisableCtorValidation());
}
