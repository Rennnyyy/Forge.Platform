namespace Forge.Branch;

/// <summary>
/// Exposes the frozen-set refresh capability of
/// <see cref="SnapshotGuardedTransactionalStore"/> to application services
/// such as <see cref="BranchSeedingService"/> without leaking the internal
/// decorator type. See Branch ADR-0002.
/// </summary>
public interface ISnapshotFrozenSetInvalidator
{
    /// <summary>
    /// Rebuilds the frozen named-graph IRI set by re-querying all
    /// <see cref="Snapshot"/> entities from the management store.
    /// Must be called after any transaction that creates or deletes a snapshot.
    /// </summary>
    ValueTask InvalidateFrozenSetAsync(CancellationToken cancellationToken = default);
}
