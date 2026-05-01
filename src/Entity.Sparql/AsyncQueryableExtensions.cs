using System.Linq.Expressions;
using System.Reflection;

namespace Forge.Entity.Sparql;

/// <summary>
/// EF-Core-shaped async terminal operators for <see cref="SparqlQueryable{T}"/>. Each
/// method composes a terminal LINQ call (Count / Any / First / …) onto the source
/// expression tree and dispatches it to <see cref="SparqlQueryProvider{T}"/>.
/// </summary>
public static class AsyncQueryableExtensions
{
    // ── ToListAsync / ToArrayAsync ──────────────────────────────────────────

    public static async ValueTask<List<T>> ToListAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        var (provider, expression) = Decompose(source);
        return await provider.ExecuteAsync<List<T>>(expression, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<T[]> ToArrayAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        var list = await source.ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.ToArray();
    }

    // ── Count / LongCount ───────────────────────────────────────────────────

    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => Terminal<T, int>(source, nameof(Queryable.Count), null, cancellationToken);

    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => Terminal<T, int>(source, nameof(Queryable.Count), predicate, cancellationToken);

    public static ValueTask<long> LongCountAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => Terminal<T, long>(source, nameof(Queryable.LongCount), null, cancellationToken);

    public static ValueTask<long> LongCountAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => Terminal<T, long>(source, nameof(Queryable.LongCount), predicate, cancellationToken);

    // ── Any / All ───────────────────────────────────────────────────────────

    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => Terminal<T, bool>(source, nameof(Queryable.Any), null, cancellationToken);

    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => Terminal<T, bool>(source, nameof(Queryable.Any), predicate, cancellationToken);

    public static ValueTask<bool> AllAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => Terminal<T, bool>(source, nameof(Queryable.All), predicate, cancellationToken);

    // ── First / Single ──────────────────────────────────────────────────────

    public static async ValueTask<T> FirstAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => await SingleShape(source, null, requireSingle: false, allowEmpty: false, cancellationToken)
            .ConfigureAwait(false) ?? throw EmptySequence();

    public static async ValueTask<T> FirstAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => await SingleShape(source, predicate, requireSingle: false, allowEmpty: false, cancellationToken)
            .ConfigureAwait(false) ?? throw EmptySequence();

    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => SingleShape(source, null, requireSingle: false, allowEmpty: true, cancellationToken);

    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => SingleShape(source, predicate, requireSingle: false, allowEmpty: true, cancellationToken);

    public static async ValueTask<T> SingleAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => await SingleShape(source, null, requireSingle: true, allowEmpty: false, cancellationToken)
            .ConfigureAwait(false) ?? throw EmptySequence();

    public static async ValueTask<T> SingleAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => await SingleShape(source, predicate, requireSingle: true, allowEmpty: false, cancellationToken)
            .ConfigureAwait(false) ?? throw EmptySequence();

    public static ValueTask<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => SingleShape(source, null, requireSingle: true, allowEmpty: true, cancellationToken);

    public static ValueTask<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => SingleShape(source, predicate, requireSingle: true, allowEmpty: true, cancellationToken);

    // ── Plumbing ────────────────────────────────────────────────────────────

    private static async ValueTask<T?> SingleShape<T>(IQueryable<T> source,
        Expression<Func<T, bool>>? predicate, bool requireSingle, bool allowEmpty,
        CancellationToken cancellationToken) where T : class, IEntity
    {
        var name = requireSingle
            ? (allowEmpty ? nameof(Queryable.SingleOrDefault) : nameof(Queryable.Single))
            : (allowEmpty ? nameof(Queryable.FirstOrDefault) : nameof(Queryable.First));
        var (provider, expression) = Decompose(source);
        var terminal = ComposeTerminal<T>(expression, name, predicate);
        return await provider.ExecuteAsync<T?>(terminal, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask<TResult> Terminal<T, TResult>(IQueryable<T> source, string method,
        Expression<Func<T, bool>>? predicate, CancellationToken cancellationToken)
        where T : class, IEntity
    {
        var (provider, expression) = Decompose(source);
        var terminal = ComposeTerminal<T>(expression, method, predicate);
        return provider.ExecuteAsync<TResult>(terminal, cancellationToken);
    }

    private static Expression ComposeTerminal<T>(Expression source, string method,
        Expression<Func<T, bool>>? predicate)
    {
        var queryable = typeof(Queryable);
        if (predicate is null)
        {
            var mi = queryable.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == method && m.IsGenericMethod && m.GetParameters().Length == 1)
                .MakeGenericMethod(typeof(T));
            return Expression.Call(null, mi, source);
        }
        else
        {
            var mi = queryable.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == method && m.IsGenericMethod && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T));
            return Expression.Call(null, mi, source, predicate);
        }
    }

    private static (SparqlQueryProvider<T> Provider, Expression Expression) Decompose<T>(
        IQueryable<T> source) where T : class, IEntity
    {
        if (source.Provider is not SparqlQueryProvider<T> provider)
            throw new InvalidOperationException(
                "AsyncQueryableExtensions can only be used on SPARQL-backed queryables produced by " +
                "EntityOperations.Query<T>() or store.Query<T>().");
        return (provider, source.Expression);
    }

    private static InvalidOperationException EmptySequence() =>
        new("Sequence contains no matching elements.");
}
