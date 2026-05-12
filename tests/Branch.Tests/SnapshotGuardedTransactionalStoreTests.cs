using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Unit tests for <see cref="SnapshotGuardedTransactionalStore"/> in isolation —
/// no DI container, no backend required. A <see cref="CapturingStore"/> tracks calls.
///
/// <list type="bullet">
///   <item>Update targeting a frozen IRI is rejected.</item>
///   <item>Create targeting a frozen IRI is rejected.</item>
///   <item>DropGraph alone on a frozen IRI is rejected.</item>
///   <item>Delete + DropGraph pair on the same frozen IRI is permitted (cascade delete).</item>
///   <item>Non-frozen operations are forwarded to the inner store.</item>
///   <item>Frozen set is empty before initialisation — no false positives.</item>
///   <item>InvalidateFrozenSetAsync rebuilds the set from the inner store.</item>
///   <item>SetFrozenIris updates the set immediately.</item>
///   <item>Exception carries the correct snapshot IRI.</item>
/// </list>
/// </summary>
public sealed class SnapshotGuardedTransactionalStoreTests
{
    private const string SnapshotIri = "https://forge-it.net/branches/v1.0.0";
    private const string OtherIri = "https://forge-it.net/branches/main";

    private static SnapshotGuardedTransactionalStore BuildGuard(out CapturingStore inner)
    {
        inner = new CapturingStore();
        return new SnapshotGuardedTransactionalStore(inner);
    }

    private static SnapshotGuardedTransactionalStore BuildFrozenGuard(
        out CapturingStore inner,
        string frozenIri = SnapshotIri)
    {
        var guard = BuildGuard(out inner);
        guard.SetFrozenIris([frozenIri]);
        return guard;
    }

    // Snapshot entity used as the target of Create/Update operations.
    private static Snapshot MakeSnapshot(string name = "v1.0.0") =>
        new Snapshot
        {
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            SnapshotAt = DateTimeOffset.UtcNow,
        };

    // ════════════════════════════════════════════════════════════════════════
    // 1. Frozen set empty — no false positives before initialisation
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Empty_frozen_set_permits_all_operations()
    {
        var guard = BuildGuard(out var inner);
        var snapshot = MakeSnapshot();

        var ops = new List<TransactionOperation>
        {
            new UpdateOperation<Snapshot>(snapshot),
            new DropGraphOperation(snapshot.Iri),
        };

        await guard.ExecuteTransactionAsync(ops);

        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Update targeting a frozen IRI is rejected
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteTransaction_blocks_Update_targeting_frozen_IRI()
    {
        var guard = BuildFrozenGuard(out _);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        // Override guard's frozen set to match the snapshot's actual IRI
        guard.SetFrozenIris([snapshot.Iri]);

        var ops = new List<TransactionOperation> { new UpdateOperation<Snapshot>(snapshot) };

        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    [Fact]
    public async Task ExecuteTransaction_Update_on_frozen_IRI_has_correct_SnapshotIri()
    {
        var guard = BuildFrozenGuard(out _, SnapshotIri);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        guard.SetFrozenIris([snapshot.Iri]);

        var ops = new List<TransactionOperation> { new UpdateOperation<Snapshot>(snapshot) };

        var ex = await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());

        ex.SnapshotIri.ShouldBe(snapshot.Iri);
    }

    [Fact]
    public async Task ExecuteTransaction_inner_not_called_when_Update_is_blocked()
    {
        var guard = BuildGuard(out var inner);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        guard.SetFrozenIris([snapshot.Iri]);

        var ops = new List<TransactionOperation> { new UpdateOperation<Snapshot>(snapshot) };

        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());

        inner.ExecuteTransactionCalled.ShouldBeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Create targeting a frozen IRI is rejected
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteTransaction_blocks_Create_targeting_frozen_IRI()
    {
        var guard = BuildGuard(out _);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        guard.SetFrozenIris([snapshot.Iri]);

        var ops = new List<TransactionOperation> { new CreateOperation<Snapshot>(snapshot) };

        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. DropGraph alone on a frozen IRI is rejected
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteTransaction_blocks_DropGraph_alone_on_frozen_IRI()
    {
        var guard = BuildGuard(out _);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        guard.SetFrozenIris([snapshot.Iri]);

        var ops = new List<TransactionOperation> { new DropGraphOperation(snapshot.Iri) };

        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Delete + DropGraph pair on the same frozen IRI is permitted
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteTransaction_permits_Delete_plus_DropGraph_pair_on_frozen_IRI()
    {
        var guard = BuildGuard(out var inner);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        guard.SetFrozenIris([snapshot.Iri]);

        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(snapshot.Iri),
            new DropGraphOperation(snapshot.Iri),
        };

        // Must not throw; inner must be called.
        await guard.ExecuteTransactionAsync(ops);

        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteTransaction_blocks_DropGraph_when_Delete_targets_different_IRI()
    {
        var guard = BuildGuard(out _);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();
        guard.SetFrozenIris([snapshot.Iri]);

        // Delete is for OtherIri, DropGraph is for the frozen snapshot — not a valid pair.
        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(OtherIri),
            new DropGraphOperation(snapshot.Iri),
        };

        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Non-frozen operations are forwarded unchanged
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteTransaction_forwards_non_frozen_operations()
    {
        var guard = BuildFrozenGuard(out var inner);
        // OtherIri is NOT frozen
        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(OtherIri),
            new DropGraphOperation(OtherIri),
        };

        await guard.ExecuteTransactionAsync(ops);

        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. SetFrozenIris updates the set immediately
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetFrozenIris_blocks_previously_allowed_IRI()
    {
        var guard = BuildGuard(out _);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();

        // Before freeze: allowed
        var allowedOps = new List<TransactionOperation> { new DropGraphOperation(snapshot.Iri) };
        await guard.ExecuteTransactionAsync(allowedOps); // must not throw

        // After freeze: rejected
        guard.SetFrozenIris([snapshot.Iri]);
        var blockedOps = new List<TransactionOperation> { new DropGraphOperation(snapshot.Iri) };

        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(blockedOps).AsTask());
    }

    [Fact]
    public async Task SetFrozenIris_allows_previously_frozen_IRI_after_removal()
    {
        var guard = BuildGuard(out var inner);
        var snapshot = MakeSnapshot();
        snapshot.MaterializeIdentity();

        guard.SetFrozenIris([snapshot.Iri]);

        // Remove from frozen set
        guard.SetFrozenIris([]); // empty — nothing frozen

        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(snapshot.Iri),
            new DropGraphOperation(snapshot.Iri),
        };

        await guard.ExecuteTransactionAsync(ops); // must not throw
        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. InvalidateFrozenSetAsync rebuilds the set from the inner store
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidateFrozenSetAsync_populates_set_from_inner_store()
    {
        // Arrange: inner store that returns one Snapshot when queried by type.
        var snapshot = MakeSnapshot("v2.0.0");
        snapshot.MaterializeIdentity();

        var queryStore = new QueryCapturingStore(snapshot);
        var guard = new SnapshotGuardedTransactionalStore(queryStore);

        // Act
        await guard.InvalidateFrozenSetAsync();

        // Assert: the snapshot's IRI is now frozen.
        var ops = new List<TransactionOperation> { new DropGraphOperation(snapshot.Iri) };
        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    [Fact]
    public async Task InvalidateFrozenSetAsync_clears_stale_entries()
    {
        var snapshot = MakeSnapshot("v3.0.0");
        snapshot.MaterializeIdentity();

        // Start with the IRI frozen manually.
        var emptyStore = new QueryCapturingStore(); // returns no snapshots
        var guard = new SnapshotGuardedTransactionalStore(emptyStore);
        guard.SetFrozenIris([snapshot.Iri]);

        // Invalidate — the store returns nothing, so the IRI should be unfrozen.
        await guard.InvalidateFrozenSetAsync();

        var ops = new List<TransactionOperation> { new DropGraphOperation(snapshot.Iri) };
        await guard.ExecuteTransactionAsync(ops); // must not throw
        emptyStore.ExecuteTransactionCalled.ShouldBeTrue();
    }
}

// ────────────────────────────────────────────────────────────────
// Test helper: a CapturingStore that returns a configured list of
// Snapshot entities from QueryByTypeAsync<Snapshot>().
// ────────────────────────────────────────────────────────────────
internal sealed class QueryCapturingStore : CapturingStore
{
    private readonly Snapshot[] _snapshots;

    internal QueryCapturingStore(params Snapshot[] snapshots) => _snapshots = snapshots;

    public override IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
    {
        if (typeof(T) == typeof(Snapshot))
            return (IAsyncEnumerable<T>)_snapshots.ToAsyncEnumerable();
        return AsyncEnumerable.Empty<T>();
    }
}
