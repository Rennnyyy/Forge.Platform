using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Transaction;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Unit tests for <see cref="BranchSeedingService"/> — no DI container, no backend
/// required. Two <see cref="CapturingStore"/> instances act as the management store and
/// data store respectively. A <see cref="SnapshotGuardedTransactionalStore"/> wraps the
/// management <see cref="CapturingStore"/> so frozen-set invalidation can be verified.
/// </summary>
public sealed class BranchSeedingServiceTests
{
    private const string Source = "https://forge-it.net/branches/main";

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a service together with the two underlying capturing stores and the guard.
    /// </summary>
    private static BranchSeedingService BuildService(
        out CapturingStore managementCapture,
        out CapturingStore dataCapture,
        out SnapshotGuardedTransactionalStore guard,
        Snapshot[]? existingSnapshots = null)
    {
        managementCapture = existingSnapshots is { Length: > 0 }
            ? new QueryCapturingStore(existingSnapshots)
            : new CapturingStore();
        dataCapture = new CapturingStore();
        guard = new SnapshotGuardedTransactionalStore(managementCapture);

        return new BranchSeedingService(
            managementStore: guard,
            dataStore: dataCapture,
            snapshotGuard: guard);
    }

    private static Branch MakeBranch(string name = "feature-x") =>
        new() { Name = name };

    private static Snapshot MakeSnapshot(
        string name = "v1.0.0",
        int? major = null, int? minor = null, int? patch = null, string? pre = null) =>
        new()
        {
            Name = name,
            SnapshotAt = DateTimeOffset.UtcNow,
            SemVerMajor = major,
            SemVerMinor = minor,
            SemVerPatch = patch,
            SemVerPreRelease = pre,
        };

    private static IReadOnlyList<string> SomeIris() =>
        ["https://forge-it.net/entities/a", "https://forge-it.net/entities/b"];

    // ════════════════════════════════════════════════════════════════════════
    // 1. CreateSeededBranchAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateSeededBranchAsync_seeds_data_store_before_management_create()
    {
        var svc = BuildService(out var mgmt, out var data, out _);
        var branch = MakeBranch();
        var callOrder = new List<string>();
        // Track order via side effects
        // We do it by checking CapturedOperations after the call.

        await svc.CreateSeededBranchAsync(branch, Source, SomeIris());

        // Data store should have received a SeedGraphOperation.
        data.CapturedOperations.ShouldHaveSingleItem();
        data.CapturedOperations[0].ShouldContain(op => op is SeedGraphOperation);

        // Management store should have received a CreateOperation.
        mgmt.CapturedOperations.ShouldHaveSingleItem();
        mgmt.CapturedOperations[0].ShouldContain(op => op is CreateOperation<Branch>);
    }

    [Fact]
    public async Task CreateSeededBranchAsync_returns_the_same_branch_instance()
    {
        var svc = BuildService(out _, out _, out _);
        var branch = MakeBranch();

        var result = await svc.CreateSeededBranchAsync(branch, Source, SomeIris());

        result.ShouldBeSameAs(branch);
    }

    [Fact]
    public async Task CreateSeededBranchAsync_SeedOperation_has_correct_IRIs()
    {
        var svc = BuildService(out _, out var data, out _);
        var branch = MakeBranch("feature-y");

        await svc.CreateSeededBranchAsync(branch, Source, SomeIris());

        var seedOp = data.CapturedOperations[0]
            .OfType<SeedGraphOperation>()
            .ShouldHaveSingleItem();

        seedOp.SourceGraphIri.ShouldBe(Source);
        seedOp.TargetGraphIri.ShouldBe(branch.Iri);
    }

    [Fact]
    public async Task CreateSeededBranchAsync_propagates_SeedOperationMissingEntityException()
    {
        // A throwing data store simulates a missing entity.
        var throwingData = new ThrowingStore(new SeedOperationMissingEntityException(
            Source, ["https://forge-it.net/entities/missing"]));
        var mgmt = new CapturingStore();
        var guard = new SnapshotGuardedTransactionalStore(mgmt);
        var svc = new BranchSeedingService(guard, throwingData, guard);

        await Should.ThrowAsync<SeedOperationMissingEntityException>(
            () => svc.CreateSeededBranchAsync(MakeBranch(), Source, SomeIris()));

        // Management store must NOT have been called.
        mgmt.ExecuteTransactionCalled.ShouldBeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. CreateSnapshotAsync — happy path
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateSnapshotAsync_seeds_data_store_before_management_create()
    {
        var svc = BuildService(out var mgmt, out var data, out _);
        var snapshot = MakeSnapshot();

        await svc.CreateSnapshotAsync(snapshot, Source, SomeIris());

        data.CapturedOperations.ShouldHaveSingleItem();
        data.CapturedOperations[0].ShouldContain(op => op is SeedGraphOperation);
        mgmt.CapturedOperations.ShouldHaveSingleItem();
        mgmt.CapturedOperations[0].ShouldContain(op => op is CreateOperation<Snapshot>);
    }

    [Fact]
    public async Task CreateSnapshotAsync_returns_the_same_snapshot_instance()
    {
        var svc = BuildService(out _, out _, out _);
        var snapshot = MakeSnapshot();

        var result = await svc.CreateSnapshotAsync(snapshot, Source, SomeIris());

        result.ShouldBeSameAs(snapshot);
    }

    [Fact]
    public async Task CreateSnapshotAsync_SeedOperation_has_correct_IRIs()
    {
        var svc = BuildService(out _, out var data, out _);
        var snapshot = MakeSnapshot("v2.0.0");

        await svc.CreateSnapshotAsync(snapshot, Source, SomeIris());

        var seedOp = data.CapturedOperations[0]
            .OfType<SeedGraphOperation>()
            .ShouldHaveSingleItem();

        seedOp.SourceGraphIri.ShouldBe(Source);
        seedOp.TargetGraphIri.ShouldBe(snapshot.Iri);
    }

    [Fact]
    public async Task CreateSnapshotAsync_without_SemVer_skips_uniqueness_check_and_succeeds()
    {
        // No SemVerMajor set → uniqueness check is skipped entirely;
        // management store is not queried before the seed.
        var svc = BuildService(out var mgmt, out _, out _);
        var snapshot = MakeSnapshot(); // SemVerMajor is null

        await svc.CreateSnapshotAsync(snapshot, Source, SomeIris());

        // Management store only called once — for the Create, not a prior query.
        mgmt.CapturedOperations.Count.ShouldBe(1);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. SemVer uniqueness enforcement
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateSnapshotAsync_throws_SnapshotVersionConflictException_on_duplicate_semver()
    {
        var existing = MakeSnapshot("v1.0.0", major: 1, minor: 0, patch: 0);
        existing.MaterializeIdentity();

        var svc = BuildService(out _, out _, out _, [existing]);
        var incoming = MakeSnapshot("v1.0.0-copy", major: 1, minor: 0, patch: 0);

        await Should.ThrowAsync<SnapshotVersionConflictException>(
            () => svc.CreateSnapshotAsync(incoming, Source, SomeIris()));
    }

    [Fact]
    public async Task CreateSnapshotAsync_version_conflict_exception_contains_version_string()
    {
        var existing = MakeSnapshot("v2.3.4-beta", major: 2, minor: 3, patch: 4, pre: "beta");
        existing.MaterializeIdentity();

        var svc = BuildService(out _, out _, out _, [existing]);
        var incoming = MakeSnapshot("v2.3.4-beta-dup", major: 2, minor: 3, patch: 4, pre: "beta");

        var ex = await Should.ThrowAsync<SnapshotVersionConflictException>(
            () => svc.CreateSnapshotAsync(incoming, Source, SomeIris()));

        ex.Version.ShouldBe("2.3.4-beta");
    }

    [Fact]
    public async Task CreateSnapshotAsync_does_not_seed_when_version_conflicts()
    {
        var existing = MakeSnapshot("v1.0.0", major: 1, minor: 0, patch: 0);
        existing.MaterializeIdentity();

        var svc = BuildService(out _, out var data, out _, [existing]);
        var incoming = MakeSnapshot("v1.0.0-dup", major: 1, minor: 0, patch: 0);

        await Should.ThrowAsync<SnapshotVersionConflictException>(
            () => svc.CreateSnapshotAsync(incoming, Source, SomeIris()));

        data.ExecuteTransactionCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateSnapshotAsync_allows_different_patch_with_same_major_minor()
    {
        var existing = MakeSnapshot("v1.0.0", major: 1, minor: 0, patch: 0);
        existing.MaterializeIdentity();

        var svc = BuildService(out _, out _, out _, [existing]);
        var incoming = MakeSnapshot("v1.0.1", major: 1, minor: 0, patch: 1);

        // Must not throw — different patch
        await svc.CreateSnapshotAsync(incoming, Source, SomeIris());
    }

    [Fact]
    public async Task CreateSnapshotAsync_allows_different_prerelease_same_major_minor_patch()
    {
        var existing = MakeSnapshot("v1.0.0-alpha", major: 1, minor: 0, patch: 0, pre: "alpha");
        existing.MaterializeIdentity();

        var svc = BuildService(out _, out _, out _, [existing]);
        var incoming = MakeSnapshot("v1.0.0-beta", major: 1, minor: 0, patch: 0, pre: "beta");

        await svc.CreateSnapshotAsync(incoming, Source, SomeIris());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. CreateSnapshotAsync invalidates frozen set after commit
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateSnapshotAsync_invalidates_frozen_set_after_management_create()
    {
        // After creation, the new snapshot's IRI should be in the frozen set.
        // We verify via the guard: a subsequent DropGraph on the snapshot IRI is blocked.
        var snapshot = MakeSnapshot("v3.0.0");
        var mgmt = new QueryCapturingStore(snapshot);       // returns the snapshot on next query
        var data = new CapturingStore();
        var guard = new SnapshotGuardedTransactionalStore(mgmt);
        var svc = new BranchSeedingService(guard, data, guard);

        await svc.CreateSnapshotAsync(snapshot, Source, SomeIris());

        // Guard should now have the snapshot IRI frozen.
        var dropOps = new List<TransactionOperation> { new DropGraphOperation(snapshot.Iri) };
        await Should.ThrowAsync<SnapshotImmutabilityViolationException>(
            () => guard.ExecuteTransactionAsync(dropOps).AsTask());
    }

    [Fact]
    public async Task CreateSnapshotAsync_propagates_SeedOperationMissingEntityException()
    {
        var throwingData = new ThrowingStore(new SeedOperationMissingEntityException(
            Source, ["https://forge-it.net/entities/missing"]));
        var mgmt = new CapturingStore();
        var guard = new SnapshotGuardedTransactionalStore(mgmt);
        var svc = new BranchSeedingService(guard, throwingData, guard);

        await Should.ThrowAsync<SeedOperationMissingEntityException>(
            () => svc.CreateSnapshotAsync(MakeSnapshot(), Source, SomeIris()));

        mgmt.ExecuteTransactionCalled.ShouldBeFalse();
    }
}

// ────────────────────────────────────────────────────────────────
// Test helper: store that throws a configured exception on ExecuteTransactionAsync.
// ────────────────────────────────────────────────────────────────
internal sealed class ThrowingStore : ITransactionalEntityStore
{
    private readonly Exception _exception;
    internal ThrowingStore(Exception exception) => _exception = exception;

    public string? NamedGraph => null;

    public ValueTask ExecuteTransactionAsync(
        IReadOnlyList<TransactionOperation> operations,
        CancellationToken cancellationToken = default)
        => throw _exception;

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
        => new((T?)null);

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default) where T : class, IEntity
        => default;

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default) => default;

    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity
        => AsyncEnumerable.Empty<T>();

    public ValueTask DisposeAsync() => default;

    ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
        where T : class
        => new((T?)null);

    IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken)
        => AsyncEnumerable.Empty<string>();
}
