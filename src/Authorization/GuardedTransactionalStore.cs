using Forge.Aspects.Abstractions;
using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Authorization;

/// <summary>
/// Decorates an <see cref="ITransactionalEntityStore"/> with pre-commit authorization
/// via an <see cref="IAspectGuard"/>. See Validation ADR-0004.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Transaction authorization</strong> — <see cref="IAspectGuard.AuthorizeAsync"/> is
/// called once per operation <em>before</em> the inner store is contacted. If the guard
/// throws for any operation, the inner store is never called and the whole transaction
/// is discarded.
/// </para>
/// <para>
/// <strong>Query authorization</strong> — <see cref="LoadAsync{T}"/> and
/// <see cref="QueryByTypeAsync{T}"/> call <see cref="IAspectGuard.AuthorizeAsync"/> with
/// <c>aspectToken = "noop"</c> before delegating to the inner store.
/// </para>
/// <para>
/// Individual-write methods (<c>SaveAsync</c>, <c>DeleteAsync</c>) on <see cref="IEntityStore"/>
/// delegate directly without a guard call; the primary write API is
/// <see cref="ITransactionalEntityStore.ExecuteTransactionAsync"/>.
/// </para>
/// </remarks>
public sealed class GuardedTransactionalStore : ITransactionalEntityStore
{
    private readonly ITransactionalEntityStore _inner;
    private readonly IAspectGuard _guard;

    /// <summary>
    /// Wraps <paramref name="inner"/> with authorization via <paramref name="guard"/>.
    /// </summary>
    public GuardedTransactionalStore(ITransactionalEntityStore inner, IAspectGuard guard)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(guard);
        _inner = inner;
        _guard = guard;
    }

    // ------------------------------------------------------------------ ITransactionalEntityStore

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="IAspectGuard.AuthorizeAsync"/> once per operation before
    /// delegating to the inner store. The agent token is resolved from
    /// when no scope is active (safe for use with <see cref="AllowAllAspectGuard"/>).
    /// If the guard throws for any operation, the inner store is never contacted.
    /// </remarks>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var agentToken = AuthorizationContext.CurrentAgentToken ?? string.Empty;
        foreach (var op in operations)
            await _guard.AuthorizeAsync(agentToken, op.AspectIri, cancellationToken)
                .ConfigureAwait(false);
        await _inner.ExecuteTransactionAsync(operations, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ IEntityStore — reads (guarded)

    /// <inheritdoc/>
    public async ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var agentToken = AuthorizationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeAsync(agentToken, Aspect.NoOpIri, cancellationToken)
            .ConfigureAwait(false);
        return await _inner.LoadAsync<T>(iri, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<T> QueryByTypeAsync<T>(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var agentToken = AuthorizationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeAsync(agentToken, Aspect.NoOpIri, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var item in _inner.QueryByTypeAsync<T>(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    // ------------------------------------------------------------------ IEntityStore — writes (guarded)

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="IAspectGuard.AuthorizeAsync"/> with <c>aspectToken = "noop"</c>
    /// before delegating to the inner store.
    /// </remarks>
    public async ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        var agentToken = AuthorizationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeAsync(agentToken, Aspect.NoOpIri, cancellationToken)
            .ConfigureAwait(false);
        await _inner.SaveAsync(entity, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="IAspectGuard.AuthorizeAsync"/> with <c>aspectToken = "noop"</c>
    /// before delegating.
    /// </remarks>
    public async ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        var agentToken = AuthorizationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeAsync(agentToken, Aspect.NoOpIri, cancellationToken)
            .ConfigureAwait(false);
        await _inner.DeleteAsync(iri, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string? NamedGraph => _inner.NamedGraph;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ------------------------------------------------------------------ IEntityLoader / ICollectionLoader

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => LoadAsync<T>(iri, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="IAspectGuard.AuthorizeAsync"/> with <c>aspectToken = "noop"</c>
    /// before streaming collection member IRIs from the inner store.
    /// This closes the authorization gap that would otherwise allow deferred
    /// <see cref="Forge.Entity.EntityRef{T}"/> and collection loading to bypass the guard.
    /// </remarks>
    async IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agentToken = AuthorizationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeAsync(agentToken, Aspect.NoOpIri, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var iri in ((ICollectionLoader)_inner)
            .LoadCollectionIrisAsync<T>(ownerIri, predicate, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return iri;
        }
    }
}
