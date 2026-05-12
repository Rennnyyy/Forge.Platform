using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Branch;

/// <summary>
/// Decorates the data-graph <see cref="ITransactionalEntityStore"/> to prevent entity
/// writes into frozen snapshot named graphs. Checks <see cref="BranchScope.Current"/>
/// against the frozen snapshot set maintained by the management-graph
/// <see cref="SnapshotGuardedTransactionalStore"/> and blocks any
/// <see cref="EntityWriteOperation"/> or <see cref="DeleteOperation"/> when the
/// current branch is a frozen snapshot IRI.
///
/// <para>
/// Graph-level operations such as <see cref="SeedGraphOperation"/> and
/// <see cref="DropGraphOperation"/> are not blocked here: seeds only run during snapshot
/// creation (before the graph is added to the frozen set) and drops only target mutable
/// branches via <c>DELETE api/branches?iri=…</c>.
/// </para>
/// See Branch ADR-0002.
/// </summary>
internal sealed class DataSnapshotGuardedTransactionalStore : ITransactionalEntityStore
{
    private readonly ITransactionalEntityStore _inner;
    private readonly SnapshotGuardedTransactionalStore _snapshotGuard;

    internal DataSnapshotGuardedTransactionalStore(
        ITransactionalEntityStore inner,
        SnapshotGuardedTransactionalStore snapshotGuard)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(snapshotGuard);
        _inner = inner;
        _snapshotGuard = snapshotGuard;
    }

    // ------------------------------------------------------------------ ITransactionalEntityStore

    /// <inheritdoc/>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var currentBranch = BranchScope.Current;
        if (currentBranch is not null && _snapshotGuard.IsFrozen(currentBranch))
        {
            foreach (var op in operations)
            {
                if (op is EntityWriteOperation or DeleteOperation)
                    throw SnapshotImmutabilityViolationException.WriteBlocked(currentBranch);
            }
        }

        await _inner.ExecuteTransactionAsync(operations, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ IEntityStore — reads

    /// <inheritdoc/>
    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.LoadAsync<T>(iri, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.QueryByTypeAsync<T>(cancellationToken);

    /// <inheritdoc/>
    public string? NamedGraph => _inner.NamedGraph;

    // ------------------------------------------------------------------ IEntityStore — writes

    /// <inheritdoc/>
    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => _inner.SaveAsync(entity, mode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(iri, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ------------------------------------------------------------------ IEntityLoader / ICollectionLoader

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => _inner.LoadAsync<T>(iri, cancellationToken);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => ((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(ownerIri, predicate, cancellationToken);
}
