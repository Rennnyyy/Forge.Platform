using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Validation;

/// <summary>
/// Decorates an <see cref="ITransactionalEntityStore"/> with pre-commit authorization
/// via an <see cref="IOperationGuard"/>. See Validation ADR-0003.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Transaction authorization</strong> — all operations are passed to
/// <see cref="IOperationGuard.AuthorizeTransactionAsync"/> <em>before</em> the inner
/// store is contacted. If the guard throws, the inner store is never called and all
/// operations are discarded.
/// </para>
/// <para>
/// <strong>Query authorization</strong> — <see cref="LoadAsync{T}"/> and
/// <see cref="QueryByTypeAsync{T}"/> call
/// <see cref="IOperationGuard.AuthorizeQueryAsync"/> with <c>aspectToken = "noop"</c>
/// before delegating to the inner store.
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
    private readonly IOperationGuard _guard;

    /// <summary>
    /// Wraps <paramref name="inner"/> with authorization via <paramref name="guard"/>.
    /// </summary>
    public GuardedTransactionalStore(ITransactionalEntityStore inner, IOperationGuard guard)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(guard);
        _inner = inner;
        _guard = guard;
    }

    // ------------------------------------------------------------------ ITransactionalEntityStore

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <see cref="IOperationGuard.AuthorizeTransactionAsync"/> with the full
    /// operations list before delegating to the inner store. The agent token is resolved
    /// from <see cref="ValidationContext.CurrentAgentToken"/>; an empty string is passed
    /// when no scope is active (safe for use with <see cref="AllowAllOperationGuard"/>).
    /// </remarks>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var agentToken = ValidationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeTransactionAsync(agentToken, operations, cancellationToken)
            .ConfigureAwait(false);
        await _inner.ExecuteTransactionAsync(operations, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ IEntityStore — reads (guarded)

    /// <inheritdoc/>
    public async ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var agentToken = ValidationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeQueryAsync(agentToken, Aspect.NoOp.Name, cancellationToken)
            .ConfigureAwait(false);
        return await _inner.LoadAsync<T>(iri, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<T> QueryByTypeAsync<T>(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        var agentToken = ValidationContext.CurrentAgentToken ?? string.Empty;
        await _guard.AuthorizeQueryAsync(agentToken, Aspect.NoOp.Name, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var item in _inner.QueryByTypeAsync<T>(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    // ------------------------------------------------------------------ IEntityStore — writes (delegated)

    /// <inheritdoc/>
    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.SaveAsync(entity, mode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(iri, cancellationToken);

    /// <inheritdoc/>
    public string? NamedGraph => _inner.NamedGraph;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ------------------------------------------------------------------ IEntityLoader / ICollectionLoader

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => LoadAsync<T>(iri, cancellationToken);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => ((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(ownerIri, predicate, cancellationToken);
}
