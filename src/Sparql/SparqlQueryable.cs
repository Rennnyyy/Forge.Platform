using Forge.Entity;
using System.Collections;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Forge.Repository;

namespace Forge.Sparql;

/// <summary>
/// User-facing handle returned by <c>store.Query&lt;T&gt;()</c>. Implements the standard
/// LINQ surface (<see cref="IOrderedQueryable{T}"/>) plus
/// <see cref="IAsyncEnumerable{T}"/> for EF-Core-shaped async streaming.
/// </summary>
public sealed class SparqlQueryable<T> : IOrderedQueryable<T>, IAsyncEnumerable<T>
    where T : class, IEntity
{
    private readonly SparqlQueryProvider<T> _provider;
    private readonly Expression _expression;

    internal SparqlQueryable(SparqlQueryProvider<T> provider, Expression? expression = null)
    {
        _provider = provider;
        _expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _provider;

    public IEnumerator<T> GetEnumerator()
    {
        // EF-Core-style sync execution: block on the async pipeline.
        return _provider.ExecuteEntities(_expression)
            .ToBlockingEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => _provider.ExecuteEntities(_expression, cancellationToken).GetAsyncEnumerator(cancellationToken);
}

/// <summary>
/// LINQ <see cref="IQueryProvider"/> for a specific entity store. One instance is created
/// per <c>store.Query&lt;T&gt;()</c> root; non-terminal calls thread the same provider
/// through subsequent <see cref="SparqlQueryable{T}"/> instances.
/// </summary>
public sealed class SparqlQueryProvider<T> : IQueryProvider where T : class, IEntity
{
    private readonly IEntityStore _store;
    private readonly ISparqlQueryStore _sparql;

    internal SparqlQueryProvider(IEntityStore store, ISparqlQueryStore sparql)
    {
        _store = store;
        _sparql = sparql;
    }

    public IQueryable CreateQuery(Expression expression) =>
        (IQueryable)CreateQueryGeneric(expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (typeof(TElement) != typeof(T))
            throw new NotSupportedException(
                $"The SPARQL provider only materializes '{typeof(T).Name}'; " +
                $"projections to '{typeof(TElement).Name}' are not supported in v1.");
        return (IQueryable<TElement>)(object)new SparqlQueryable<T>(this, expression);
    }

    private object CreateQueryGeneric(Expression expression) =>
        new SparqlQueryable<T>(this, expression);

    public object? Execute(Expression expression) => Execute<object>(expression);

    public TResult Execute<TResult>(Expression expression)
        => ExecuteAsync<TResult>(expression, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Async terminal evaluation. Invoked from <see cref="AsyncQueryableExtensions"/>.</summary>
    internal async ValueTask<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken ct)
    {
        var model = LinqToSparqlVisitor.Build<T>(expression);

        switch (model.Terminal)
        {
            case SparqlTerminalKind.Count:
                {
                    var n = await CountAsync(model, ct).ConfigureAwait(false);
                    return ConvertCount<TResult>(n);
                }
            case SparqlTerminalKind.Any:
                {
                    var present = await AnyPresentAsync(model, ct).ConfigureAwait(false);
                    var result = model.AllInverted ? !present : present;
                    return (TResult)(object)result;
                }
            case SparqlTerminalKind.Entities:
                if (model.Single != SingleResultMode.None)
                {
                    var single = await SingleResultAsync(model, ct).ConfigureAwait(false);
                    return (TResult)(object)single!;
                }
                // Materialize fully then return as List<T> if requested.
                {
                    var list = new List<T>();
                    await foreach (var item in ExecuteEntitiesInternal(model, ct).ConfigureAwait(false))
                        list.Add(item);
                    return (TResult)(object)list;
                }
            default:
                throw new InvalidOperationException("Unknown SPARQL terminal kind.");
        }
    }

    /// <summary>Async-enumerable entity stream. Invoked from <see cref="SparqlQueryable{T}"/>.</summary>
    internal IAsyncEnumerable<T> ExecuteEntities(Expression expression, CancellationToken ct = default)
    {
        var model = LinqToSparqlVisitor.Build<T>(expression);
        if (model.Terminal != SparqlTerminalKind.Entities)
            throw new InvalidOperationException(
                "Cannot enumerate entities from a Count/Any-shaped query. " +
                "Use the corresponding async terminal (CountAsync / AnyAsync) instead.");
        return ExecuteEntitiesInternal(model, ct);
    }

    private async IAsyncEnumerable<T> ExecuteEntitiesInternal(SparqlQueryModel model,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sparql = SparqlEmitter.Emit(model);
        await foreach (var row in _sparql.ExecuteSelectAsync(sparql, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var iri = row.GetIri(SparqlEmitter.SubjectVar);
            if (iri is null) continue;
            var loaded = await _store.LoadAsync<T>(iri, ct).ConfigureAwait(false);
            if (loaded is not null) yield return loaded;
        }
    }

    private async ValueTask<long> CountAsync(SparqlQueryModel model, CancellationToken ct)
    {
        var sparql = SparqlEmitter.Emit(model);
        await foreach (var row in _sparql.ExecuteSelectAsync(sparql, ct).ConfigureAwait(false))
        {
            var lex = row.GetLiteral(SparqlEmitter.CountVar);
            if (lex is not null && long.TryParse(lex, System.Globalization.CultureInfo.InvariantCulture, out var n))
                return n;
        }
        return 0;
    }

    private async ValueTask<bool> AnyPresentAsync(SparqlQueryModel model, CancellationToken ct)
    {
        var sparql = SparqlEmitter.Emit(model);
        await foreach (var _ in _sparql.ExecuteSelectAsync(sparql, ct).ConfigureAwait(false))
            return true;
        return false;
    }

    private async ValueTask<T?> SingleResultAsync(SparqlQueryModel model, CancellationToken ct)
    {
        var sparql = SparqlEmitter.Emit(model);
        var iris = new List<string>(2);
        await foreach (var row in _sparql.ExecuteSelectAsync(sparql, ct).ConfigureAwait(false))
        {
            var iri = row.GetIri(SparqlEmitter.SubjectVar);
            if (iri is not null) iris.Add(iri);
            if (model.Single == SingleResultMode.Single && iris.Count > 1) break;
            if (model.Single == SingleResultMode.First && iris.Count >= 1) break;
        }
        if (iris.Count == 0)
            // FirstOrDefault / SingleOrDefault are handled by callers via TResult conversion;
            // First / Single throw when empty — surfaced by AsyncQueryableExtensions.
            return null;
        if (model.Single == SingleResultMode.Single && iris.Count > 1)
            throw new InvalidOperationException(
                "Sequence contains more than one matching element (Single / SingleOrDefault).");
        return await _store.LoadAsync<T>(iris[0], ct).ConfigureAwait(false);
    }

    private static TResult ConvertCount<TResult>(long n)
    {
        if (typeof(TResult) == typeof(int))  return (TResult)(object)checked((int)n);
        if (typeof(TResult) == typeof(long)) return (TResult)(object)n;
        return (TResult)Convert.ChangeType(n, typeof(TResult), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
