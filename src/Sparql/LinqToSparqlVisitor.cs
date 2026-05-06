using Forge.Entity;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Forge.Sparql;

/// <summary>
/// Walks a LINQ expression tree built on top of <see cref="SparqlQueryable{T}"/> and
/// builds a <see cref="SparqlQueryModel"/>.
/// </summary>
/// <remarks>
/// v1 surface is documented in <c>Forge.Sparql/adr/0001-linq-to-sparql-provider.md</c>.
/// Anything outside that surface throws <see cref="NotSupportedException"/> with a
/// message naming the offending node.
/// </remarks>
internal static class LinqToSparqlVisitor
{
    public static SparqlQueryModel Build<T>(Expression expression) where T : class, IEntity
    {
        var map = EntityPredicateMap<T>.Instance;
        var model = new SparqlQueryModel { TypeIri = map.TypeIri };
        Walk(expression, model, map);
        return model;
    }

    private static void Walk<T>(Expression e, SparqlQueryModel model, EntityPredicateMap<T> map)
        where T : class, IEntity
    {
        switch (e)
        {
            case ConstantExpression:
                return;
            case MethodCallExpression mc when mc.Method.DeclaringType == typeof(Queryable):
                Walk(mc.Arguments[0], model, map);
                ApplyOperator(mc, model, map);
                return;
            default:
                throw new NotSupportedException(
                    $"Unsupported root expression in SPARQL query tree: {e.NodeType}.");
        }
    }

    private static void ApplyOperator<T>(MethodCallExpression mc, SparqlQueryModel model,
        EntityPredicateMap<T> map) where T : class, IEntity
    {
        switch (mc.Method.Name)
        {
            case nameof(Queryable.Where):
                AddFilter(mc.Arguments[1], model, map);
                return;

            case nameof(Queryable.OrderBy):
            case nameof(Queryable.ThenBy):
                AddOrdering(mc.Arguments[1], model, map, descending: false);
                return;

            case nameof(Queryable.OrderByDescending):
            case nameof(Queryable.ThenByDescending):
                AddOrdering(mc.Arguments[1], model, map, descending: true);
                return;

            case nameof(Queryable.Skip):
                model.Skip = (model.Skip ?? 0) + (int)((ConstantExpression)mc.Arguments[1]).Value!;
                return;

            case nameof(Queryable.Take):
                {
                    var n = (int)((ConstantExpression)mc.Arguments[1]).Value!;
                    model.Take = model.Take is null ? n : Math.Min(model.Take.Value, n);
                    return;
                }

            case nameof(Queryable.Select):
                {
                    var lambda = (LambdaExpression)StripQuotes(mc.Arguments[1]);
                    if (lambda.Body is ParameterExpression p && p == lambda.Parameters[0])
                        return; // identity Select — no-op
                    throw new NotSupportedException(
                        "Only identity 'Select(x => x)' is supported in v1 (no projections). " +
                        "See Sparql ADR-0001.");
                }

            case nameof(Queryable.Count):
            case nameof(Queryable.LongCount):
                if (mc.Arguments.Count == 2)
                    AddFilter(mc.Arguments[1], model, map);
                model.Terminal = SparqlTerminalKind.Count;
                return;

            case nameof(Queryable.Any):
                if (mc.Arguments.Count == 2)
                    AddFilter(mc.Arguments[1], model, map);
                model.Terminal = SparqlTerminalKind.Any;
                return;

            case nameof(Queryable.All):
                {
                    if (mc.Arguments.Count != 2)
                        throw new NotSupportedException("All requires a predicate.");
                    // All(p) ≡ !Any(!p) — translate as Any with negated predicate; the
                    // provider inverts the result on terminal evaluation.
                    var ctx = new TranslationContext<T>(((LambdaExpression)StripQuotes(mc.Arguments[1])).Parameters[0], map);
                    var inner = TranslateBoolean(((LambdaExpression)StripQuotes(mc.Arguments[1])).Body, ctx);
                    foreach (var b in ctx.Referenced.Values) model.Reference(b);
                    model.Filters.Add($"!({inner})");
                    model.Terminal = SparqlTerminalKind.Any;
                    model.AllInverted = true;
                    return;
                }

            case nameof(Queryable.First):
            case nameof(Queryable.FirstOrDefault):
                if (mc.Arguments.Count == 2)
                    AddFilter(mc.Arguments[1], model, map);
                model.Single = SingleResultMode.First;
                return;

            case nameof(Queryable.Single):
            case nameof(Queryable.SingleOrDefault):
                if (mc.Arguments.Count == 2)
                    AddFilter(mc.Arguments[1], model, map);
                model.Single = SingleResultMode.Single;
                return;

            default:
                throw new NotSupportedException(
                    $"LINQ operator '{mc.Method.Name}' is not supported by the SPARQL provider in v1.");
        }
    }

    private static void AddFilter<T>(Expression lambdaArg, SparqlQueryModel model,
        EntityPredicateMap<T> map) where T : class, IEntity
    {
        var lambda = (LambdaExpression)StripQuotes(lambdaArg);
        var ctx = new TranslationContext<T>(lambda.Parameters[0], map);
        var s = TranslateBoolean(lambda.Body, ctx);
        foreach (var b in ctx.Referenced.Values) model.Reference(b);
        model.Filters.Add(s);
    }

    private static void AddOrdering<T>(Expression keySelectorExpr, SparqlQueryModel model,
        EntityPredicateMap<T> map, bool descending) where T : class, IEntity
    {
        var lambda = (LambdaExpression)StripQuotes(keySelectorExpr);
        if (lambda.Body is not MemberExpression me ||
            me.Expression is not ParameterExpression p || p != lambda.Parameters[0])
            throw new NotSupportedException(
                "OrderBy / ThenBy must use a direct property reference: x => x.Property.");
        var binding = ResolveBinding(me.Member.Name, map);
        model.Reference(binding);
        model.Orderings.Add(new SparqlQueryModel.Ordering(binding, descending));
    }

    // ── Filter translation ──────────────────────────────────────────────────

    private static string TranslateBoolean<T>(Expression expr, TranslationContext<T> ctx)
        where T : class, IEntity
    {
        switch (expr.NodeType)
        {
            case ExpressionType.AndAlso:
                {
                    var b = (BinaryExpression)expr;
                    return $"({TranslateBoolean(b.Left, ctx)} && {TranslateBoolean(b.Right, ctx)})";
                }
            case ExpressionType.OrElse:
                {
                    var b = (BinaryExpression)expr;
                    return $"({TranslateBoolean(b.Left, ctx)} || {TranslateBoolean(b.Right, ctx)})";
                }
            case ExpressionType.Not:
                return $"(!{TranslateBoolean(((UnaryExpression)expr).Operand, ctx)})";

            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
                return TranslateComparison((BinaryExpression)expr, ctx);

            case ExpressionType.Call:
                return TranslateMethodCall((MethodCallExpression)expr, ctx);

            case ExpressionType.MemberAccess:
                {
                    // A bare boolean property: x.Active  →  ?v_Active = "true"^^xsd:boolean
                    if (expr is MemberExpression me && me.Expression is ParameterExpression p
                        && p == ctx.Parameter && me.Type == typeof(bool))
                    {
                        var binding = ctx.Resolve(me.Member.Name);
                        return $"(?{binding.Variable} = \"true\"^^<{XsdBoolean}>)";
                    }
                    throw new NotSupportedException(
                        "Boolean member access must be on the lambda parameter.");
                }
            case ExpressionType.Constant:
                {
                    var v = ((ConstantExpression)expr).Value;
                    return v is true ? "true" : "false";
                }
            default:
                throw new NotSupportedException(
                    $"Unsupported boolean expression node: {expr.NodeType}.");
        }
    }

    private static string TranslateComparison<T>(BinaryExpression b, TranslationContext<T> ctx)
        where T : class, IEntity
    {
        // Detect null comparisons specially so we can emit BOUND / !BOUND.
        if (IsNullConstant(b.Right) && b.Left is MemberExpression mLeft)
            return EmitNullCheck(mLeft, ctx, isEqual: b.NodeType == ExpressionType.Equal);
        if (IsNullConstant(b.Left) && b.Right is MemberExpression mRight)
            return EmitNullCheck(mRight, ctx, isEqual: b.NodeType == ExpressionType.Equal);

        // IRI-based subject equality: x.Iri ==/!= "..."
        if (TryIriCompare(b.Left, b.Right, ctx, b.NodeType, out var iriExpr)) return iriExpr;
        if (TryIriCompare(b.Right, b.Left, ctx, b.NodeType, out iriExpr)) return iriExpr;

        var (left, expectedType) = TranslateOperand(b.Left, ctx);
        var (right, _) = TranslateOperand(b.Right, ctx, expectedType);
        var op = b.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            _ => throw new NotSupportedException($"Comparison '{b.NodeType}' is not supported."),
        };
        return $"({left} {op} {right})";
    }

    private static string EmitNullCheck<T>(MemberExpression me, TranslationContext<T> ctx, bool isEqual)
        where T : class, IEntity
    {
        if (me.Expression is not ParameterExpression p || p != ctx.Parameter)
            throw new NotSupportedException("Null comparison must be on the lambda parameter's property.");
        if (me.Member.Name == nameof(IEntity.Iri))
            throw new NotSupportedException("Subject IRI is never null; remove the null comparison.");
        var binding = ctx.Resolve(me.Member.Name);
        return isEqual ? $"!BOUND(?{binding.Variable})" : $"BOUND(?{binding.Variable})";
    }

    private static bool TryIriCompare<T>(Expression member, Expression other, TranslationContext<T> ctx,
        ExpressionType op, out string filter) where T : class, IEntity
    {
        filter = "";
        if (member is not MemberExpression me) return false;
        if (me.Expression is not ParameterExpression p || p != ctx.Parameter) return false;
        if (me.Member.Name != nameof(IEntity.Iri)) return false;
        var rhs = EvaluateConstant(other);
        if (rhs is not string s)
            throw new NotSupportedException("Iri can only be compared to a string constant.");
        var sym = op switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            _ => throw new NotSupportedException("IRI comparison only supports '==' / '!='."),
        };
        filter = $"(?s {sym} <{s}>)";
        return true;
    }

    private static (string Sparql, Type? ExpectedType) TranslateOperand<T>(
        Expression expr, TranslationContext<T> ctx, Type? expected = null) where T : class, IEntity
    {
        // Property reference?
        if (expr is MemberExpression me && me.Expression is ParameterExpression p && p == ctx.Parameter)
        {
            if (me.Member.Name == nameof(IEntity.Iri))
                return ("?s", typeof(string));
            var binding = ctx.Resolve(me.Member.Name);
            var clr = Nullable.GetUnderlyingType(binding.ClrType) ?? binding.ClrType;
            return ($"?{binding.Variable}", clr);
        }

        // Otherwise: evaluate constant and emit literal
        var val = EvaluateConstant(expr);
        return (EmitConstant(val, expected), expected ?? val?.GetType());
    }

    private static string TranslateMethodCall<T>(MethodCallExpression mc, TranslationContext<T> ctx)
        where T : class, IEntity
    {
        // string.Contains / StartsWith / EndsWith on a property
        if (mc.Object is MemberExpression me && me.Expression is ParameterExpression p && p == ctx.Parameter
            && mc.Method.DeclaringType == typeof(string))
        {
            var binding = ctx.Resolve(me.Member.Name);
            var arg = EvaluateConstant(mc.Arguments[0]);
            if (arg is not string s)
                throw new NotSupportedException(
                    $"string.{mc.Method.Name} requires a string constant argument in v1.");
            var fn = mc.Method.Name switch
            {
                nameof(string.Contains) => "CONTAINS",
                nameof(string.StartsWith) => "STRSTARTS",
                nameof(string.EndsWith) => "STRENDS",
                _ => throw new NotSupportedException(
                    $"string.{mc.Method.Name} is not supported by the SPARQL provider."),
            };
            return $"{fn}(STR(?{binding.Variable}), {EmitStringLiteral(s)})";
        }

        // Bool-returning Equals(other)?
        if (mc.Method.Name == nameof(object.Equals) && mc.Arguments.Count == 1 && mc.Object is not null)
        {
            var fakeBin = Expression.Equal(mc.Object, mc.Arguments[0]);
            return TranslateComparison(fakeBin, ctx);
        }

        throw new NotSupportedException(
            $"Method call '{mc.Method.DeclaringType?.Name}.{mc.Method.Name}' is not supported in a SPARQL filter.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Expression StripQuotes(Expression e)
    {
        while (e.NodeType == ExpressionType.Quote)
            e = ((UnaryExpression)e).Operand;
        return e;
    }

    private static bool IsNullConstant(Expression e)
    {
        if (e is ConstantExpression c && c.Value is null) return true;
        if (e is UnaryExpression u && u.NodeType == ExpressionType.Convert && u.Operand is ConstantExpression cc && cc.Value is null) return true;
        return false;
    }

    private static object? EvaluateConstant(Expression e)
    {
        if (e is ConstantExpression c) return c.Value;
        var lambda = Expression.Lambda(Expression.Convert(e, typeof(object)));
        return lambda.Compile().DynamicInvoke();
    }

    private static PropertyBinding ResolveBinding<T>(string propertyName, EntityPredicateMap<T> map)
        where T : class, IEntity
    {
        if (!map.TryGet(propertyName, out var b))
            throw new NotSupportedException(
                $"Property '{typeof(T).Name}.{propertyName}' is not annotated with [Predicate] " +
                "and cannot be used in a SPARQL query filter or ordering.");
        return b;
    }

    // ── SPARQL literal emission ─────────────────────────────────────────────

    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";
    private const string XsdInt = "http://www.w3.org/2001/XMLSchema#int";
    private const string XsdLong = "http://www.w3.org/2001/XMLSchema#long";
    private const string XsdDouble = "http://www.w3.org/2001/XMLSchema#double";
    private const string XsdFloat = "http://www.w3.org/2001/XMLSchema#float";
    private const string XsdDecimal = "http://www.w3.org/2001/XMLSchema#decimal";
    private const string XsdDateTime = "http://www.w3.org/2001/XMLSchema#dateTime";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    private static string EmitConstant(object? value, Type? expectedType)
    {
        if (value is null)
            throw new NotSupportedException(
                "Unexpected null operand. Use 'x.Property == null' / '!= null' for null checks.");

        var t = expectedType is null
            ? value.GetType()
            : Nullable.GetUnderlyingType(expectedType) ?? expectedType;

        if (t == typeof(string)) return EmitTypedLiteral((string)value, XsdString);
        if (t == typeof(bool)) return EmitTypedLiteral(((bool)value) ? "true" : "false", XsdBoolean);
        if (t == typeof(int)) return EmitTypedLiteral(Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), XsdInt);
        if (t == typeof(long)) return EmitTypedLiteral(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), XsdLong);
        if (t == typeof(short)) return EmitTypedLiteral(Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), XsdInt);
        if (t == typeof(double)) return EmitTypedLiteral(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture), XsdDouble);
        if (t == typeof(float)) return EmitTypedLiteral(Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture), XsdFloat);
        if (t == typeof(decimal)) return EmitTypedLiteral(Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), XsdDecimal);
        if (t == typeof(Guid)) return EmitTypedLiteral(((Guid)value).ToString("D"), XsdString);
        if (t == typeof(DateTime)) return EmitTypedLiteral(((DateTime)value).ToString("o", CultureInfo.InvariantCulture), XsdDateTime);
        if (t == typeof(DateTimeOffset)) return EmitTypedLiteral(((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture), XsdDateTime);
        if (t == typeof(DateOnly)) return EmitTypedLiteral(((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), XsdDate);
        if (t == typeof(Uri)) return $"<{((Uri)value)}>";
        return EmitTypedLiteral(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", XsdString);
    }

    private static string EmitTypedLiteral(string lex, string datatypeIri) =>
        $"\"{EscapeSparqlString(lex)}\"^^<{datatypeIri}>";

    private static string EmitStringLiteral(string lex) =>
        $"\"{EscapeSparqlString(lex)}\"";

    private static string EscapeSparqlString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    // ── Translation context ─────────────────────────────────────────────────

    private sealed class TranslationContext<T> where T : class, IEntity
    {
        public ParameterExpression Parameter { get; }
        public EntityPredicateMap<T> Map { get; }
        public Dictionary<string, PropertyBinding> Referenced { get; } = new(StringComparer.Ordinal);

        public TranslationContext(ParameterExpression parameter, EntityPredicateMap<T> map)
        {
            Parameter = parameter;
            Map = map;
        }

        public PropertyBinding Resolve(string propertyName)
        {
            var b = ResolveBinding(propertyName, Map);
            Referenced[b.Name] = b;
            return b;
        }
    }
}
