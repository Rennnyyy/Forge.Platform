using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Branch;

/// <summary>
/// Orchestrates the creation of content-seeded branches and snapshots.
/// Combines a management-graph entity write (<see cref="CreateOperation{T}"/>) with a
/// data-graph population (<see cref="SeedGraphOperation"/>) in a correlated two-step
/// commit, and enforces SemVer uniqueness for <see cref="Snapshot"/> entities.
/// See Branch ADR-0003.
/// </summary>
/// <remarks>
/// The two operations target different stores and are committed sequentially:
/// <see cref="SeedGraphOperation"/> first, then the management <c>Create</c>. If the
/// seed fails the management write is never issued. If the management write fails after
/// a successful seed, the seeded graph becomes an orphan until a subsequent attempt or
/// manual cleanup.
/// </remarks>
public sealed class BranchSeedingService
{
    private readonly ITransactionalEntityStore _managementStore;
    private readonly ITransactionalEntityStore _dataStore;
    private readonly ISnapshotFrozenSetInvalidator _snapshotGuard;

    public BranchSeedingService(
        [FromKeyedServices("forge.branch.management")] ITransactionalEntityStore managementStore,
        ITransactionalEntityStore dataStore,
        [FromKeyedServices("forge.branch.management")] ISnapshotFrozenSetInvalidator snapshotGuard)
    {
        ArgumentNullException.ThrowIfNull(managementStore);
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(snapshotGuard);
        _managementStore = managementStore;
        _dataStore = dataStore;
        _snapshotGuard = snapshotGuard;
    }

    /// <summary>
    /// Creates a new <see cref="Branch"/> entity in the management graph and populates
    /// its named graph by copying the triples for <paramref name="entityIris"/> from
    /// <paramref name="sourceGraphIri"/>.
    /// </summary>
    /// <param name="branch">The branch entity to create. Must have a valid <see cref="Branch.Name"/>.</param>
    /// <param name="sourceGraphIri">The named graph to copy entity triples from.</param>
    /// <param name="entityIris">The explicit list of entity IRIs to include in the new graph.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created branch entity (same instance as <paramref name="branch"/>).</returns>
    /// <exception cref="SeedOperationMissingEntityException">
    /// Thrown when one or more IRIs in <paramref name="entityIris"/> are absent from
    /// <paramref name="sourceGraphIri"/>. No entity is created.
    /// </exception>
    public async Task<Branch> CreateSeededBranchAsync(
        Branch branch,
        string sourceGraphIri,
        IReadOnlyList<string> entityIris,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceGraphIri);
        ArgumentNullException.ThrowIfNull(entityIris);

        // 1. Seed the named graph in the data store.
        await using (var seedTx = new EntityTransaction(_dataStore))
        {
            seedTx.SeedFrom(sourceGraphIri, branch.Iri, entityIris);
            await seedTx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // 2. Create the management entity.
        await using var mgmtTx = new EntityTransaction(_managementStore);
        mgmtTx.Create(branch);
        await mgmtTx.CommitAsync(cancellationToken).ConfigureAwait(false);

        return branch;
    }

    /// <summary>
    /// Creates a new <see cref="Snapshot"/> entity in the management graph and populates
    /// its named graph by copying the triples for <paramref name="entityIris"/> from
    /// <paramref name="sourceGraphIri"/>. Enforces SemVer uniqueness and refreshes the
    /// immutability guard after a successful commit.
    /// </summary>
    /// <param name="snapshot">The snapshot entity to create.</param>
    /// <param name="sourceGraphIri">The named graph to copy entity triples from.</param>
    /// <param name="entityIris">The explicit list of entity IRIs to include in the snapshot graph.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created snapshot entity (same instance as <paramref name="snapshot"/>).</returns>
    /// <exception cref="SnapshotVersionConflictException">
    /// Thrown when the SemVer tuple of <paramref name="snapshot"/> already exists.
    /// </exception>
    /// <exception cref="SeedOperationMissingEntityException">
    /// Thrown when one or more IRIs in <paramref name="entityIris"/> are absent from
    /// <paramref name="sourceGraphIri"/>. No snapshot is created.
    /// </exception>
    public async Task<Snapshot> CreateSnapshotAsync(
        Snapshot snapshot,
        string sourceGraphIri,
        IReadOnlyList<string> entityIris,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceGraphIri);
        ArgumentNullException.ThrowIfNull(entityIris);

        // 1. SemVer uniqueness check (only when at least one SemVer property is set).
        if (snapshot.SemVerMajor is not null)
            await CheckSemVerUniquenessAsync(snapshot, cancellationToken).ConfigureAwait(false);

        // 2. Seed the named graph in the data store.
        await using (var seedTx = new EntityTransaction(_dataStore))
        {
            seedTx.SeedFrom(sourceGraphIri, snapshot.Iri, entityIris);
            await seedTx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // 3. Create the management entity.
        await using var mgmtTx = new EntityTransaction(_managementStore);
        mgmtTx.Create(snapshot);
        await mgmtTx.CommitAsync(cancellationToken).ConfigureAwait(false);

        // 4. Refresh frozen set so the immutability guard is immediately aware.
        await _snapshotGuard.InvalidateFrozenSetAsync(cancellationToken).ConfigureAwait(false);

        return snapshot;
    }

    // ------------------------------------------------------------------ private

    /// <summary>
    /// Deletes a <see cref="Snapshot"/> from the management graph, drops its named graph,
    /// and refreshes the frozen-set guard so the deleted IRI is no longer considered frozen.
    /// </summary>
    /// <param name="snapshot">The snapshot entity to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task DeleteSnapshotAsync(
        Snapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // 1. Delete the snapshot entity from the management graph.
        await using var mgmtTx = new EntityTransaction(_managementStore);
        mgmtTx.Delete(snapshot.Iri);
        await mgmtTx.CommitAsync(cancellationToken).ConfigureAwait(false);

        // 2. Drop the snapshot's data graph (same two-store pattern as branch delete).
        using var _ = BranchScope.Use(snapshot.Iri);
        await using var dataTx = new EntityTransaction(_dataStore);
        dataTx.DropGraph(snapshot.Iri);
        await dataTx.CommitAsync(cancellationToken).ConfigureAwait(false);

        await _snapshotGuard.InvalidateFrozenSetAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask CheckSemVerUniquenessAsync(
        Snapshot snapshot,
        CancellationToken cancellationToken)
    {
        await foreach (var existing in _managementStore
            .QueryByTypeAsync<Snapshot>(cancellationToken)
            .ConfigureAwait(false))
        {
            if (SemVerTupleMatches(existing, snapshot))
            {
                throw SnapshotVersionConflictException.Duplicate(
                    snapshot.SemVerMajor!.Value,
                    snapshot.SemVerMinor,
                    snapshot.SemVerPatch,
                    snapshot.SemVerPreRelease);
            }
        }
    }

    private static bool SemVerTupleMatches(Snapshot a, Snapshot b)
        => a.SemVerMajor == b.SemVerMajor
        && a.SemVerMinor == b.SemVerMinor
        && a.SemVerPatch == b.SemVerPatch
        && string.Equals(a.SemVerPreRelease, b.SemVerPreRelease, StringComparison.Ordinal);
}
