using Forge.Branch.Merge;
using Forge.Repository;
using Forge.Repository.Transaction;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Unit tests for <see cref="BranchMergeService"/>. See Branch ADR-0007.
/// Uses stub implementations of <see cref="IBranchDiffEngine"/> and
/// <see cref="IMergePlanner"/> so no real store or SPARQL engine is needed.
/// </summary>
public sealed class BranchMergeServiceTests
{
    private const string Source = "https://forge-it.net/branches/source";
    private const string Target = "https://forge-it.net/branches/target";

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubDiffEngine : IBranchDiffEngine
    {
        public EntityGraphDelta Delta { get; set; } = new(Source, Target, []);

        public Task<EntityGraphDelta> ComputeDiffAsync(
            string source, string target, CancellationToken ct = default)
            => Task.FromResult(Delta);
    }

    private sealed class StubMergePlanner : IMergePlanner
    {
        public IReadOnlyList<TransactionOperation> Operations { get; set; } = [];

        public Task<IReadOnlyList<TransactionOperation>> PlanAsync(
            EntityGraphDelta delta,
            IEntityStore sourceStore,
            IEntityStore targetStore,
            CancellationToken ct = default)
            => Task.FromResult(Operations);
    }

    private static BranchMergeService Build(
        out StubDiffEngine engine,
        out StubMergePlanner planner,
        out CapturingStore store)
    {
        engine = new StubDiffEngine();
        planner = new StubMergePlanner();
        store = new CapturingStore();
        return new BranchMergeService(engine, planner, store);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_throws_ArgumentException_when_source_equals_target()
    {
        var svc = Build(out _, out _, out _);

        await Should.ThrowAsync<ArgumentException>(
            () => svc.MergeAsync(Source, Source));
    }

    [Fact]
    public async Task MergeAsync_returns_empty_result_when_diff_is_empty()
    {
        var svc = Build(out var engine, out _, out var store);
        engine.Delta = new EntityGraphDelta(Source, Target, []);

        var result = await svc.MergeAsync(Source, Target);

        result.IsEmpty.ShouldBeTrue();
        result.SourceBranchIri.ShouldBe(Source);
        result.TargetBranchIri.ShouldBe(Target);
        store.ExecuteTransactionCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task MergeAsync_executes_planned_operations_as_single_transaction()
    {
        var svc = Build(out var engine, out var planner, out var store);

        // A non-empty delta triggers planning + execution.
        var branch = new Branch { Name = "my-branch", CreatedAt = DateTimeOffset.UtcNow };
        engine.Delta = new EntityGraphDelta(Source, Target, [
            new EntityDeltaEntry(branch.Iri, "https://forge-it.net/types/Branch", EntityDeltaKind.Added),
        ]);

        var expectedOp = new CreateOperation<Branch>(branch);
        planner.Operations = [expectedOp];

        await svc.MergeAsync(Source, Target);

        store.ExecuteTransactionCalled.ShouldBeTrue();
        store.CapturedOperations.Count.ShouldBe(1);
        store.CapturedOperations[0].ShouldContain(expectedOp);
    }

    [Fact]
    public async Task MergeAsync_returns_correct_created_and_updated_counts()
    {
        var svc = Build(out var engine, out var planner, out _);

        var b1 = new Branch { Name = "b1", CreatedAt = DateTimeOffset.UtcNow };
        var b2 = new Branch { Name = "b2", CreatedAt = DateTimeOffset.UtcNow };
        var b3 = new Branch { Name = "b3", CreatedAt = DateTimeOffset.UtcNow };

        engine.Delta = new EntityGraphDelta(Source, Target, [
            new EntityDeltaEntry(b1.Iri, "https://forge-it.net/types/Branch", EntityDeltaKind.Added),
            new EntityDeltaEntry(b2.Iri, "https://forge-it.net/types/Branch", EntityDeltaKind.Added),
            new EntityDeltaEntry(b3.Iri, "https://forge-it.net/types/Branch", EntityDeltaKind.Modified),
        ]);

        planner.Operations = [
            new CreateOperation<Branch>(b1),
            new CreateOperation<Branch>(b2),
            new UpdateOperation<Branch>(b3),
        ];

        var result = await svc.MergeAsync(Source, Target);

        result.CreatedCount.ShouldBe(2);
        result.UpdatedCount.ShouldBe(1);
        result.TotalCount.ShouldBe(3);
        result.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task MergeAsync_does_not_call_planner_when_diff_is_empty()
    {
        var svc = Build(out var engine, out var planner, out _);
        engine.Delta = new EntityGraphDelta(Source, Target, []);

        // Track calls by making planner throw if invoked.
        planner.Operations = null!; // would NPE on read if invoked

        // Should not throw — planner is not called.
        await svc.MergeAsync(Source, Target);
    }
}
