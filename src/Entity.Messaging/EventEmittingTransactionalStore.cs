using Forge.Entity;
using Forge.Execution;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Entity.Messaging;

/// <summary>
/// Decorates an <see cref="ITransactionalEntityStore"/> with entity-change event emission.
/// Sits between the authorization guard (outermost) and the aspect-enforcing tier:
/// <c>Guard → EventEmitting → AspectEnforcing → Backend</c>.
/// <para>
/// Events are emitted <em>after</em> the inner operation succeeds to guarantee that
/// only committed writes produce events (at-least-once delivery — see root ADR-0021).
/// </para>
/// <para>
/// Entity types not registered via <c>AddForgeEntityEvent&lt;TEntity&gt;</c> pass through
/// silently; the decorator does not enforce universal event emission.
/// </para>
/// </summary>
internal sealed class EventEmittingTransactionalStore : ITransactionalEntityStore
{
    private readonly ITransactionalEntityStore _inner;
    private readonly IEntityEventEmitterRegistry _registry;

    public EventEmittingTransactionalStore(
        ITransactionalEntityStore inner,
        IEntityEventEmitterRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(registry);
        _inner = inner;
        _registry = registry;
    }

    // ──────────────────────────────────────────────── ITransactionalEntityStore

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates to the inner store first. On successful commit, emits one
    /// <see cref="EntityChangedEnvelope{TDto}"/> per operation to both the history and
    /// state topics for that entity type. Operations whose entity type is not registered
    /// in the emitter registry are silently skipped.
    /// </remarks>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        await _inner.ExecuteTransactionAsync(operations, cancellationToken).ConfigureAwait(false);

        var correlation = ExecutionScope.Current ?? new ExecutionCorrelation();
        var namedGraph = _inner.NamedGraph;

        foreach (var op in operations)
            await EmitOperationAsync(op, namedGraph, correlation, cancellationToken).ConfigureAwait(false);
    }

    // ──────────────────────────────────────────────── IEntityStore — writes (emit after)

    /// <inheritdoc/>
    public async ValueTask SaveAsync<T>(
        T entity,
        WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        await _inner.SaveAsync(entity, mode, cancellationToken).ConfigureAwait(false);

        var emitter = _registry.TryGet(typeof(T));
        if (emitter is not null)
        {
            var op = mode == WriteMode.Create
                ? EntityChangeOperation.Created
                : EntityChangeOperation.Updated;
            var correlation = ExecutionScope.Current ?? new ExecutionCorrelation();
            await emitter.EmitAsync(entity, op, _inner.NamedGraph, correlation, cancellationToken)
                         .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
    {
        await _inner.DeleteAsync(iri, cancellationToken).ConfigureAwait(false);
        // DeleteAsync(iri) has no entity-type context — event emission is not possible here.
        // Callers that need a typed delete event should use ITransactionalEntityStore.ExecuteTransactionAsync
        // with a DeleteOperation<T> so the entity type is available.
    }

    // ──────────────────────────────────────────────── IEntityStore — reads (delegate)

    /// <inheritdoc/>
    public string? NamedGraph => _inner.NamedGraph;

    /// <inheritdoc/>
    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.LoadAsync<T>(iri, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => _inner.QueryByTypeAsync<T>(cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    // ──────────────────────────────────────────────── IEntityLoader / ICollectionLoader

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken ct)
        where T : class
        => _inner.LoadAsync<T>(iri, ct);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken ct)
        => ((ICollectionLoader)_inner).LoadCollectionIrisAsync<T>(ownerIri, predicate, ct);

    // ──────────────────────────────────────────────── Helpers

    private async ValueTask EmitOperationAsync(
        TransactionOperation op,
        string? namedGraph,
        ExecutionCorrelation correlation,
        CancellationToken cancellationToken)
    {
        switch (op)
        {
            case EntityWriteOperation write:
                {
                    var emitter = _registry.TryGet(write.Entity.GetType());
                    if (emitter is null) return;

                    var changeOp = write.Mode == WriteMode.Create
                        ? EntityChangeOperation.Created
                        : EntityChangeOperation.Updated;

                    await emitter.EmitAsync(write.Entity, changeOp, namedGraph, correlation, cancellationToken)
                                 .ConfigureAwait(false);
                    break;
                }

            case DeleteOperation { EntityType: { } entityType } del:
                {
                    var emitter = _registry.TryGet(entityType);
                    if (emitter is null) return;

                    await emitter.EmitDeleteAsync(del.Iri, namedGraph, correlation, cancellationToken)
                                 .ConfigureAwait(false);
                    break;
                }

            // DeleteOperation with no EntityType: no emitter lookup possible — skip silently.
            default:
                break;
        }
    }
}
