using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Branch;

/// <summary>
/// Decorates the management-graph <see cref="ITransactionalEntityStore"/> with snapshot
/// immutability invariants. Maintains an in-memory set of frozen named graph IRIs
/// (one per <see cref="Snapshot"/> entity) and rejects any write that targets a frozen
/// graph, except the sole permitted operation: a paired
/// <see cref="DeleteOperation"/> + <see cref="DropGraphOperation"/> on the same IRI
/// within the same transaction, which atomically removes the snapshot.
///
/// Frozen-set consistency strategy: startup-load + flush-on-write (Branch ADR-0002,
/// Option 3). The set is loaded once at application start by
/// <see cref="SnapshotStartupService"/> and rebuilt by <see cref="InvalidateFrozenSetAsync"/>
/// after any transaction that creates or deletes a snapshot.
///
/// All other operations are delegated to the inner store unchanged.
/// </summary>
internal sealed class SnapshotGuardedTransactionalStore : ITransactionalEntityStore, ISnapshotFrozenSetInvalidator
{
    private readonly ITransactionalEntityStore _inner;
    private HashSet<string> _frozenIris = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();

    internal SnapshotGuardedTransactionalStore(ITransactionalEntityStore inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    // ------------------------------------------------------------------ Frozen-set management

    /// <summary>
    /// Replaces the frozen-IRI set with <paramref name="iris"/>. Called by
    /// <see cref="SnapshotStartupService"/> at startup and by <see cref="InvalidateFrozenSetAsync"/>
    /// after a create/delete transaction.
    /// </summary>
    internal void SetFrozenIris(IEnumerable<string> iris)
    {
        ArgumentNullException.ThrowIfNull(iris);
        var newSet = new HashSet<string>(iris, StringComparer.Ordinal);
        _lock.EnterWriteLock();
        try { _frozenIris = newSet; }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc cref="ISnapshotFrozenSetInvalidator.InvalidateFrozenSetAsync"/>
    public async ValueTask InvalidateFrozenSetAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = new List<string>();
        await foreach (var s in _inner.QueryByTypeAsync<Snapshot>(cancellationToken).ConfigureAwait(false))
            snapshots.Add(s.Iri);
        SetFrozenIris(snapshots);
    }

    internal bool IsFrozen(string iri)
    {
        _lock.EnterReadLock();
        try { return _frozenIris.Contains(iri); }
        finally { _lock.ExitReadLock(); }
    }

    // ------------------------------------------------------------------ Guard

    private void GuardOperations(IReadOnlyList<TransactionOperation> operations)
    {
        // Collect the set of IRIs for which a Delete+DropGraph pair exists in this transaction.
        // Those paired operations are the sole permitted write against a frozen graph.
        var deleteIris = new HashSet<string>(StringComparer.Ordinal);
        var dropGraphIris = new HashSet<string>(StringComparer.Ordinal);

        foreach (var op in operations)
        {
            if (op is DeleteOperation del)
                deleteIris.Add(del.Iri);
            else if (op is DropGraphOperation drop)
                dropGraphIris.Add(drop.GraphIri);
        }

        // A Delete+DropGraph pair on the same IRI is the atomic cascade-delete pattern
        // from Branch ADR-0001 applied to snapshots.
        var pairedDeleteDrop = new HashSet<string>(deleteIris, StringComparer.Ordinal);
        pairedDeleteDrop.IntersectWith(dropGraphIris);

        foreach (var op in operations)
        {
            switch (op)
            {
                case EntityWriteOperation write when IsFrozen(write.EntityIri):
                    throw SnapshotImmutabilityViolationException.WriteBlocked(write.EntityIri);

                case DropGraphOperation drop
                    when IsFrozen(drop.GraphIri) && !pairedDeleteDrop.Contains(drop.GraphIri):
                    throw SnapshotImmutabilityViolationException.DropGraphBlocked(drop.GraphIri);
            }
        }
    }

    // ------------------------------------------------------------------ ITransactionalEntityStore

    /// <inheritdoc/>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        GuardOperations(operations);
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
    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return _inner.DisposeAsync();
    }

    // ------------------------------------------------------------------ IEntityLoader / ICollectionLoader

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => _inner.LoadAsync<T>(iri, cancellationToken);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => ((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(ownerIri, predicate, cancellationToken);
}
