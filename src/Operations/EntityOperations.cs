using Forge.Entity;
using Forge.Repository;
using Forge.Sparql;

namespace Forge.Operations;

/// <summary>
/// Ambient binding that routes entity active-record operations to an <see cref="IEntityStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Basic usage</strong> — bind a store before calling any generated entity method:
/// <code>
/// using var _ = EntityOperations.Use(myStore);
/// await artist.CreateAsync();
/// var found = await Artist.ReadAsync(iri);
/// </code>
/// </para>
/// <para>
/// <strong>DI / per-request usage</strong> — resolve the store from DI and open a scope in
/// middleware (e.g. an ASP.NET Core middleware or a Minimal API filter):
/// <code>
/// var store = scope.ServiceProvider.GetRequiredService&lt;IEntityStore&gt;();
/// using var _ = EntityOperations.Use(store);
/// await next(context);
/// </code>
/// </para>
/// </remarks>
public static class EntityOperations
{
    private static readonly AsyncLocal<IEntityStore?> _store = new();

    /// <summary>
    /// The <see cref="IEntityStore"/> bound to the current async control flow, or
    /// <see langword="null"/> if none has been bound with <see cref="Use"/>.
    /// </summary>
    public static IEntityStore? CurrentStore => _store.Value;

    /// <summary>
    /// Opens an ambient scope that routes entity operations to <paramref name="store"/>
    /// for the current async control flow. Dispose to restore the previous scope.
    /// </summary>
    public static IDisposable Use(IEntityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var previous = _store.Value;
        _store.Value = store;
        return new StoreScope(previous);
    }

    /// <summary>
    /// Returns the store bound to the current async control flow.
    /// Throws <see cref="InvalidOperationException"/> if no store has been bound.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="IEntityStore"/> is bound. Call
    /// <see cref="Use"/> before invoking entity operations.
    /// </exception>
    public static IEntityStore RequireStore()
        => _store.Value ?? throw new InvalidOperationException(
            "No EntityOperations store is bound to the current context. " +
            "Call EntityOperations.Use(store) before invoking entity operations.");

    // ── Delegation helpers called by generated entity methods ────────────────

    /// <summary>Persist a new entity; fails if a subject with the same IRI already exists.</summary>
    public static ValueTask CreateAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => RequireStore().SaveAsync(entity, WriteMode.Create, cancellationToken);

    /// <summary>Replace an existing entity (full PUT semantics).</summary>
    public static ValueTask UpdateAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => RequireStore().SaveAsync(entity, WriteMode.Replace, cancellationToken);

    /// <summary>Delete every triple whose subject is <paramref name="iri"/>.</summary>
    public static ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => RequireStore().DeleteAsync(iri, cancellationToken);

    /// <summary>Load a single entity by IRI, or <see langword="null"/> if absent.</summary>
    public static ValueTask<T?> ReadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => RequireStore().LoadAsync<T>(iri, cancellationToken);

    /// <summary>Stream all entities of type <typeparamref name="T"/> from the store.</summary>
    public static IAsyncEnumerable<T> ListAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => RequireStore().QueryByTypeAsync<T>(cancellationToken);

    /// <summary>
    /// Opens a new <see cref="EntityTransaction"/> against the ambient store. The bound store
    /// must implement <see cref="ITransactionalEntityStore"/>; otherwise a
    /// <see cref="NotSupportedException"/> is thrown.
    /// </summary>
    /// <example>
    /// <code>
    /// using var _ = EntityOperations.Use(store);
    /// await using var tx = EntityOperations.BeginTransaction();
    /// tx.Create(artist).Update(label).Delete(obsoleteIri);
    /// await tx.CommitAsync();
    /// </code>
    /// </example>
    /// <exception cref="NotSupportedException">
    /// The bound store does not implement <see cref="ITransactionalEntityStore"/>.
    /// </exception>
    public static EntityTransaction BeginTransaction()
    {
        var store = RequireStore();
        if (store is not ITransactionalEntityStore txStore)
            throw new NotSupportedException(
                $"The bound store ({store.GetType().Name}) does not implement " +
                $"{nameof(ITransactionalEntityStore)}. Transactions are not supported by this backend.");
        return new EntityTransaction(txStore);
    }

    /// <summary>
    /// Open a LINQ-shaped <see cref="IQueryable{T}"/> against the ambient store. The
    /// bound store must implement <see cref="ISparqlQueryStore"/>; otherwise a
    /// <see cref="NotSupportedException"/> is thrown. See Operations ADR-0003.
    /// </summary>
    /// <example>
    /// <code>
    /// using var _ = EntityOperations.Use(store);
    /// var artists = await EntityOperations
    ///     .Query&lt;Artist&gt;()
    ///     .Where(a =&gt; a.Country == "us" &amp;&amp; a.Active)
    ///     .OrderBy(a =&gt; a.Name)
    ///     .Take(20)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<T> Query<T>() where T : class, IEntity
        => RequireStore().Query<T>();

    // ── Internal scope ───────────────────────────────────────────────────────

    private sealed class StoreScope(IEntityStore? previous) : IDisposable
    {
        public void Dispose() => _store.Value = previous;
    }
}
