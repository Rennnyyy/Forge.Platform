using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;
using Forge.Aspects.Operation;
using System.Collections.Concurrent;

namespace Forge.Aspects;

/// <summary>
/// Decorates <see cref="ITransactionalEntityStore"/> with aspect validation.
/// For each operation in the transaction, validates LOCAL then CONTEXT before applying
/// (Aspects ADR-0001 §"Validation pipeline"). Operations are applied one at a time so
/// that SPARQL context queries observe intermediate state from earlier operations in
/// the same transaction (enabling queue-order semantics per ADR-0001).
/// See Aspects ADR-0012 for the snapshot-based compensation design.
/// </summary>
internal sealed class AspectEnforcingTransactionalStore : ITransactionalEntityStore
{
    private readonly ITransactionalEntityStore _inner;
    private readonly ISparqlQueryStore _queryStore;
    private readonly IOperationAspectEngine _engine;
    // Decorated IEntityStore (AspectEnforcingEntityStore) used for public reads so that
    // QueryAspectScope filtering applies when callers resolve ITransactionalEntityStore
    // directly. Write operations and snapshot capture continue to use _inner (the raw
    // backend) to avoid aspect interference with compensating transactions.
    private readonly IEntityStore _readStore;

    // Cache of ISnapshotCaptor singletons keyed by entity CLR type.
    // Built on first use via MakeGenericType; amortises reflection cost across calls.
    private static readonly ConcurrentDictionary<Type, ISnapshotCaptor> _captors = new();

    public AspectEnforcingTransactionalStore(
        ITransactionalEntityStore inner,
        ISparqlQueryStore queryStore,
        IOperationAspectEngine engine,
        IEntityStore readStore)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(queryStore);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(readStore);
        _inner = inner;
        _queryStore = queryStore;
        _engine = engine;
        _readStore = readStore;
    }

    // ------------------------------------------------------------------ ITransactionalEntityStore

    /// <summary>
    /// Validates and applies each operation in order. Each operation is validated (local + context)
    /// against the current store state (which includes the effects of previous operations in this
    /// same transaction) before being applied. If validation fails the already-applied operations
    /// are rolled back via compensating closures captured before each apply.
    /// See Aspects ADR-0012 for the snapshot-before-apply rollback strategy.
    /// </summary>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0) return;

        // Each entry is a closure that undoes the corresponding applied operation.
        // Pushed in forward order; popped in LIFO order during rollback.
        var undoStack = new Stack<Func<CancellationToken, ValueTask>>(operations.Count);
        try
        {
            foreach (var op in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture undo BEFORE applying — snapshot current entity state so that
                // rollback can restore it even across opaque backend transactions.
                var undo = await CaptureUndoAsync(op, cancellationToken).ConfigureAwait(false);

                // Validate against current progressive store state (LOCAL → CONTEXT).
                await _engine.ValidateAsync(op, _queryStore, cancellationToken).ConfigureAwait(false);

                // Apply via single-op inner transaction — makes state visible to subsequent SPARQL.
                await _inner.ExecuteTransactionAsync([op], cancellationToken).ConfigureAwait(false);

                // Only push after successful apply so failed-before-apply ops are not undone.
                if (undo is not null)
                    undoStack.Push(undo);
            }
        }
        catch
        {
            // Rollback: apply undo closures in LIFO order (= reverse application order).
            if (undoStack.Count > 0)
                await RollbackAsync(undoStack, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // ------------------------------------------------------------------ IEntityStore delegation

    public string? NamedGraph => _inner.NamedGraph;

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _readStore.LoadAsync<T>(iri, cancellationToken);

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.SaveAsync(entity, mode, cancellationToken);

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(iri, cancellationToken);

    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _readStore.QueryByTypeAsync<T>(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // IEntityLoader / ICollectionLoader pass-through
    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken ct)
        where T : class
        => _readStore.LoadAsync<T>(iri, ct);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken ct)
        => ((ICollectionLoader)_readStore).LoadCollectionIrisAsync<T>(ownerIri, predicate, ct);

    // ------------------------------------------------------------------ Undo capture

    private async ValueTask<Func<CancellationToken, ValueTask>?> CaptureUndoAsync(
        TransactionOperation op, CancellationToken ct)
    {
        switch (op)
        {
            case EntityWriteOperation write:
                return await GetCaptor(write.Entity.GetType())
                    .CaptureUndoAsync(_inner, write.EntityIri, OperationKind.Write, write.Mode == WriteMode.Create, ct)
                    .ConfigureAwait(false);

            case DeleteOperation { EntityType: { } entityType } del:
                return await GetCaptor(entityType)
                    .CaptureUndoAsync(_inner, del.Iri, OperationKind.Delete, isCreate: false, ct)
                    .ConfigureAwait(false);

            default:
                // DeleteOperation with no EntityType: no snapshot possible; undo is a no-op.
                return null;
        }
    }

    // ------------------------------------------------------------------ Rollback

    private static async ValueTask RollbackAsync(
        Stack<Func<CancellationToken, ValueTask>> undoStack, CancellationToken ct)
    {
        while (undoStack.TryPop(out var undo))
        {
            try
            {
                await undo(ct).ConfigureAwait(false);
            }
            catch
            {
                // Swallow — best-effort compensation; the original exception takes precedence.
            }
        }
    }

    // ------------------------------------------------------------------ Snapshot captor infrastructure

    private enum OperationKind { Write, Delete }

    private static ISnapshotCaptor GetCaptor(Type entityType)
        => _captors.GetOrAdd(entityType, t =>
            (ISnapshotCaptor)Activator.CreateInstance(
                typeof(SnapshotCaptor<>).MakeGenericType(t))!);

    /// <summary>
    /// Lets <see cref="CaptureUndoAsync"/> dispatch to a typed <c>LoadAsync&lt;T&gt;</c>
    /// call without knowing <typeparamref name="T"/> at the call site.
    /// </summary>
    private interface ISnapshotCaptor
    {
        ValueTask<Func<CancellationToken, ValueTask>?> CaptureUndoAsync(
            ITransactionalEntityStore inner,
            string iri,
            OperationKind kind,
            bool isCreate,
            CancellationToken ct);
    }

    private sealed class SnapshotCaptor<T> : ISnapshotCaptor where T : class, IEntity
    {
        public async ValueTask<Func<CancellationToken, ValueTask>?> CaptureUndoAsync(
            ITransactionalEntityStore inner,
            string iri,
            OperationKind kind,
            bool isCreate,
            CancellationToken ct)
        {
            if (isCreate)
            {
                // Undo a Create by deleting what was inserted — no load needed.
                return undoCt => inner.ExecuteTransactionAsync([new DeleteOperation(iri)], undoCt);
            }

            // For Update and Delete: snapshot current state so rollback can restore it.
            var snapshot = await inner.LoadAsync<T>(iri, ct).ConfigureAwait(false);

            if (snapshot is null)
            {
                // Entity did not exist before this operation.
                // For a Write (Update) that created the entity: undo by deleting it.
                // For a Delete on an already-absent entity: nothing to restore.
                return kind == OperationKind.Write
                    ? undoCt => inner.ExecuteTransactionAsync([new DeleteOperation(iri)], undoCt)
                    : null;
            }

            // Entity existed — undo by restoring the snapshot via a full replacement.
            return undoCt => inner.ExecuteTransactionAsync(
                [new UpdateOperation<T>(snapshot)], undoCt);
        }
    }
}
