namespace Forge.Entity.Repository;

/// <summary>
/// A unit of work that batches <see cref="TransactionOperation"/> instances into
/// a single ACID transaction. Build the list of operations, then call
/// <see cref="CommitAsync"/> to apply them atomically to the bound store.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance via <c>EntityOperations.BeginTransaction()</c> (ambient layer) or
/// by constructing directly from any <see cref="ITransactionalEntityStore"/>.
/// </para>
/// <example>
/// <code>
/// using var _ = EntityOperations.Use(store);
/// await using var tx = EntityOperations.BeginTransaction();
/// tx.Create(artist).Update(label).Delete(obsoleteIri);
/// await tx.CommitAsync();
/// </code>
/// </example>
/// </remarks>
public sealed class EntityTransaction : IAsyncDisposable
{
    private readonly ITransactionalEntityStore _store;
    private readonly List<TransactionOperation> _operations = new();
    private bool _committed;
    private bool _disposed;

    /// <summary>Initializes a transaction against the given store.</summary>
    public EntityTransaction(ITransactionalEntityStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Enqueues a Create operation for <paramref name="entity"/>. Fails at commit time
    /// if an entity with the same IRI already exists.
    /// </summary>
    public EntityTransaction Create<T>(T entity) where T : class, IEntity
    {
        ThrowIfFinished();
        ArgumentNullException.ThrowIfNull(entity);
        _operations.Add(new CreateOperation<T>(entity));
        return this;
    }

    /// <summary>Enqueues an Update (Replace) operation for <paramref name="entity"/>.</summary>
    public EntityTransaction Update<T>(T entity) where T : class, IEntity
    {
        ThrowIfFinished();
        ArgumentNullException.ThrowIfNull(entity);
        _operations.Add(new UpdateOperation<T>(entity));
        return this;
    }

    /// <summary>Enqueues a Delete operation for the entity with the given <paramref name="iri"/>.</summary>
    public EntityTransaction Delete(string iri)
    {
        ThrowIfFinished();
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        _operations.Add(new DeleteOperation(iri));
        return this;
    }

    /// <summary>
    /// Atomically applies all enqueued operations to the store. Throws
    /// <see cref="InvalidOperationException"/> if the transaction has already been committed.
    /// </summary>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed)
            throw new InvalidOperationException("This transaction has already been committed.");

        _committed = true;
        await _store.ExecuteTransactionAsync(
            _operations.AsReadOnly(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the transaction. If <see cref="CommitAsync"/> was never called, the
    /// enqueued operations are discarded — the store was never contacted.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return default;
    }

    private void ThrowIfFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed)
            throw new InvalidOperationException(
                "Cannot add operations to an already committed transaction.");
    }
}
