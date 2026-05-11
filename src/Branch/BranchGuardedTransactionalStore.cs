using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;
using System.Runtime.CompilerServices;

namespace Forge.Branch;

/// <summary>
/// Decorates the management graph <see cref="ITransactionalEntityStore"/> with branch
/// protection invariants:
/// <list type="bullet">
///   <item>
///     <see cref="Forge.Repository.Transaction.DeleteOperation"/> targeting the configured
///     default branch IRI is blocked — the default branch cannot be deleted.
///   </item>
///   <item>
///     <see cref="Forge.Repository.Transaction.DropGraphOperation"/> targeting the
///     management graph IRI is blocked — the management graph cannot be dropped.
///   </item>
/// </list>
/// All other operations are delegated to the inner store unchanged.
/// </summary>
internal sealed class BranchGuardedTransactionalStore : ITransactionalEntityStore
{
    private readonly ITransactionalEntityStore _inner;
    private readonly string _defaultBranchIri;
    private readonly string _managementGraphIri;

    internal BranchGuardedTransactionalStore(
        ITransactionalEntityStore inner,
        string defaultBranchIri,
        string managementGraphIri)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBranchIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(managementGraphIri);
        _inner = inner;
        _defaultBranchIri = defaultBranchIri;
        _managementGraphIri = managementGraphIri;
    }

    // ------------------------------------------------------------------ Guard checks

    private void GuardOperation(TransactionOperation op)
    {
        switch (op)
        {
            case DeleteOperation del
                when string.Equals(del.Iri, _defaultBranchIri, StringComparison.Ordinal):
                throw BranchProtectionViolationException.DefaultBranchDelete(del.Iri);

            case DropGraphOperation drop
                when string.Equals(drop.GraphIri, _managementGraphIri, StringComparison.Ordinal):
                throw BranchProtectionViolationException.ManagementGraphDrop(drop.GraphIri);
        }
    }

    // ------------------------------------------------------------------ ITransactionalEntityStore

    /// <inheritdoc/>
    /// <remarks>
    /// Each operation is validated against the branch protection invariants before
    /// any operation is forwarded to the inner store. If any operation violates an
    /// invariant the entire transaction is rejected and the inner store is never contacted.
    /// </remarks>
    public async ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        foreach (var op in operations)
            GuardOperation(op);

        await _inner.ExecuteTransactionAsync(operations, cancellationToken)
            .ConfigureAwait(false);
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
    /// <remarks>
    /// Blocks deletion of the default branch IRI even when called directly
    /// (outside of a transaction).
    /// </remarks>
    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        if (string.Equals(iri, _defaultBranchIri, StringComparison.Ordinal))
            throw BranchProtectionViolationException.DefaultBranchDelete(iri);
        return _inner.DeleteAsync(iri, cancellationToken);
    }

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
