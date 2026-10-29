using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper;

/// <summary>Projection expression visitors (Stage 2 internals, original code).
/// Provider limits, documented here so callers do not have to rediscover them:
///  - A visitor can only rewrite what is visible in the expression tree. A
///    compiled delegate (Condition, PreCondition, a resolver instance, a
///    ConvertUsing/ConstructUsing function) has no tree to rewrite, so it can
///    never be translated; the builder rejects those members instead of
///    embedding a delegate call that a query provider would fail on.
///  - Closure-captured values appear as a MemberExpression over a
///    ConstantExpression (the compiler-generated closure object). Most query
///    providers translate primitive closure values as parameters, but a
///    non-primitive closure object is not translatable. Pass such values
///    explicitly through the ProjectTo parameters overload, which substitutes
///    them by captured-variable name (see ClosureParameterizer).
///  - Expression.Block / locals are never emitted by the projection builder:
///    a query provider cannot translate a block, so repeated sub-expressions
///    are duplicated in the tree instead of hoisted into a variable.
/// </summary>
public static class ProjectionVisitors
{
    /// <summary>Replaces every occurrence of one parameter with a replacement
    /// expression. This is how a nested projection lambda and a MapFrom
    /// lambda are rebased onto the expression that actually produces their
    /// source value (a query parameter, a member chain, or a converted
    /// source value).</summary>
    public sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly Expression _to;
        public ParameterReplacer(ParameterExpression from, Expression to) { _from = from; _to = to; }
        public Expression Replace(Expression node) => Visit(node);
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    /// <summary>Rewrites source member access per a mapping function. Given a
    /// member-access node, the supplied function may return a replacement
    /// expression (for example, the mapped chain for that member); returning
    /// null leaves the node to be visited normally. Used by the projection
    /// builder when inlining a nested projection: member accesses on the
    /// nested lambda's parameter are rewritten onto the supplied source
    /// expression, which is exactly what <see cref="ParameterReplacer"/>
    /// does for the parameter itself — this visitor additionally lets a
    /// caller rewrite individual member nodes.</summary>
    public sealed class SourceMemberRewriter : ExpressionVisitor
    {
        private readonly Func<MemberExpression, Expression?> _rewrite;
        public SourceMemberRewriter(Func<MemberExpression, Expression?> rewrite) { _rewrite = rewrite; }
        public Expression Rewrite(Expression node) => Visit(node);
        protected override Expression VisitMember(MemberExpression node)
        {
            var rewritten = _rewrite(node);
            if (rewritten != null) return Visit(rewritten);
            return base.VisitMember(node);
        }
    }

    /// <summary>Substitutes explicitly supplied parameter values for
    /// closure-captured variables, matched by captured-variable name
    /// (case-insensitive). A captured variable is a field access on the
    /// compiler-generated closure object, i.e. a MemberExpression whose
    /// expression is a ConstantExpression. Only fields whose name appears
    /// in <paramref name="parameters"/> are replaced; all other closure
    /// accesses pass through unchanged (see the provider limits above).</summary>
    public sealed class ClosureParameterizer : ExpressionVisitor
    {
        private readonly IReadOnlyDictionary<string, object?> _parameters;
        public ClosureParameterizer(IReadOnlyDictionary<string, object?> parameters) { _parameters = parameters; }
        public Expression Parameterize(Expression node) => Visit(node);
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ConstantExpression && node.Member is FieldInfo field)
            {
                foreach (var kv in _parameters)
                {
                    if (!string.Equals(kv.Key, field.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    var value = kv.Value;
                    if (value == null) return Expression.Constant(null, field.FieldType);
                    if (field.FieldType.IsInstanceOfType(value)) return Expression.Constant(value, field.FieldType);
                    try
                    {
                        var converted = Convert.ChangeType(value, Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType);
                        return Expression.Constant(converted, field.FieldType);
                    }
                    catch { return Expression.Constant(value, field.FieldType); }
                }
            }
            return base.VisitMember(node);
        }
    }
}

/// <summary>Null-safe query rewriter: rewrites a member-access chain
/// (a.B.C, including field and property accesses) into a null-propagating
/// form, so a null anywhere along the chain yields null (or the default
/// value for a non-nullable value-type leaf) instead of throwing inside
/// the query. Only MemberExpression chains (and conversions wrapped
/// around them) are rewritten; every other node passes through unchanged.
/// The root of the chain is checked too, so a null source element yields
/// null rather than a NullReferenceException at enumeration time.</summary>
public static class NullSafeRewriter
{
    public static Expression Rewrite(Expression expression) => RewriteNode(expression);

    private static Expression RewriteNode(Expression node)
    {
        switch (node)
        {
            case MemberExpression member when member.Expression != null:
            {
                var inner = RewriteNode(member.Expression);
                var memberAccess = Expression.MakeMemberAccess(inner, member.Member);
                if (!CanBeNull(inner.Type)) return memberAccess;
                var nullBranch = member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) == null
                    ? (Expression)Expression.Default(member.Type)
                    : Expression.Constant(null, member.Type);
                return Expression.Condition(
                    Expression.Equal(inner, Expression.Constant(null, inner.Type)),
                    nullBranch,
                    memberAccess);
            }
            case UnaryExpression unary when unary.NodeType == ExpressionType.Convert
                || unary.NodeType == ExpressionType.ConvertChecked:
                return Expression.Convert(RewriteNode(unary.Operand), node.Type);
            default:
                return node;
        }
    }

    internal static bool CanBeNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
}
