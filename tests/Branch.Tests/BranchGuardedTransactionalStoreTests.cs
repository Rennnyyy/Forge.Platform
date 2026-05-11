using Forge.Repository;
using Forge.Repository.Transaction;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Unit tests for <see cref="BranchGuardedTransactionalStore"/> in isolation —
/// no DI container, no backend required. A fake inner store tracks calls.
/// </summary>
public sealed class BranchGuardedTransactionalStoreTests
{
    private const string DefaultBranchIri = "https://forge-it.net/branches/main";
    private const string ManagementGraphIri = "https://forge-it.net/management";
    private const string OtherBranchIri = "https://forge-it.net/branches/dev";
    private const string OtherGraphIri = "https://forge-it.net/data";

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static BranchGuardedTransactionalStore BuildGuard(
        out CapturingStore inner,
        string defaultBranch = DefaultBranchIri,
        string managementGraph = ManagementGraphIri)
    {
        inner = new CapturingStore();
        return new BranchGuardedTransactionalStore(inner, defaultBranch, managementGraph);
    }

    // ─── Delete default branch — transaction path ──────────────────────────────

    [Fact]
    public async Task ExecuteTransaction_blocks_Delete_of_default_branch()
    {
        var guard = BuildGuard(out _);
        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(DefaultBranchIri),
        };

        await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    [Fact]
    public async Task ExecuteTransaction_Delete_default_branch_exception_has_correct_IRI()
    {
        var guard = BuildGuard(out _);
        var ops = new List<TransactionOperation> { new DeleteOperation(DefaultBranchIri) };

        var ex = await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());

        ex.ProtectedIri.ShouldBe(DefaultBranchIri);
    }

    [Fact]
    public async Task ExecuteTransaction_blocks_default_branch_Delete_even_in_multi_op_transaction()
    {
        var guard = BuildGuard(out _);
        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(OtherBranchIri),
            new DeleteOperation(DefaultBranchIri),   // should be caught
        };

        await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    [Fact]
    public async Task ExecuteTransaction_inner_not_called_when_operation_is_blocked()
    {
        var guard = BuildGuard(out var inner);
        var ops = new List<TransactionOperation> { new DeleteOperation(DefaultBranchIri) };

        await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());

        inner.ExecuteTransactionCalled.ShouldBeFalse();
    }

    // ─── Drop management graph — transaction path ──────────────────────────────

    [Fact]
    public async Task ExecuteTransaction_blocks_DropGraph_of_management_graph()
    {
        var guard = BuildGuard(out _);
        var ops = new List<TransactionOperation>
        {
            new DropGraphOperation(ManagementGraphIri),
        };

        await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());
    }

    [Fact]
    public async Task ExecuteTransaction_DropGraph_management_exception_has_correct_IRI()
    {
        var guard = BuildGuard(out _);
        var ops = new List<TransactionOperation> { new DropGraphOperation(ManagementGraphIri) };

        var ex = await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.ExecuteTransactionAsync(ops).AsTask());

        ex.ProtectedIri.ShouldBe(ManagementGraphIri);
    }

    // ─── Allowed operations pass through ──────────────────────────────────────

    [Fact]
    public async Task ExecuteTransaction_allows_Delete_of_non_default_branch()
    {
        var guard = BuildGuard(out var inner);
        var ops = new List<TransactionOperation> { new DeleteOperation(OtherBranchIri) };

        await guard.ExecuteTransactionAsync(ops);

        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteTransaction_allows_DropGraph_of_non_management_graph()
    {
        var guard = BuildGuard(out var inner);
        var ops = new List<TransactionOperation> { new DropGraphOperation(OtherGraphIri) };

        await guard.ExecuteTransactionAsync(ops);

        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteTransaction_allows_cascade_Delete_and_DropGraph_for_non_default_branch()
    {
        var guard = BuildGuard(out var inner);
        var ops = new List<TransactionOperation>
        {
            new DeleteOperation(OtherBranchIri),
            new DropGraphOperation(OtherBranchIri),  // cascade-drop of the branch data graph
        };

        await guard.ExecuteTransactionAsync(ops);

        inner.ExecuteTransactionCalled.ShouldBeTrue();
    }

    // ─── Direct DeleteAsync path ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_blocks_default_branch_IRI()
    {
        await using var guard = BuildGuard(out _);

        await Should.ThrowAsync<BranchProtectionViolationException>(
            () => guard.DeleteAsync(DefaultBranchIri).AsTask());
    }

    [Fact]
    public async Task DeleteAsync_allows_non_default_IRI()
    {
        await using var guard = BuildGuard(out var inner);

        await guard.DeleteAsync(OtherBranchIri);

        inner.DeleteAsyncCalled.ShouldBeTrue();
    }

    // ─── NamedGraph passthrough ───────────────────────────────────────────────

    [Fact]
    public void NamedGraph_returns_inner_value()
    {
        var guard = BuildGuard(out var inner);
        inner.NamedGraphValue = ManagementGraphIri;

        guard.NamedGraph.ShouldBe(ManagementGraphIri);
    }

    // ─── Null/whitespace guards ───────────────────────────────────────────────

    [Fact]
    public void Constructor_throws_for_null_inner()
    {
        Should.Throw<ArgumentNullException>(
            () => new BranchGuardedTransactionalStore(null!, DefaultBranchIri, ManagementGraphIri));
    }

    [Fact]
    public void Constructor_throws_for_empty_defaultBranchIri()
    {
        Should.Throw<ArgumentException>(
            () => new BranchGuardedTransactionalStore(new CapturingStore(), "", ManagementGraphIri));
    }

    [Fact]
    public void Constructor_throws_for_empty_managementGraphIri()
    {
        Should.Throw<ArgumentException>(
            () => new BranchGuardedTransactionalStore(new CapturingStore(), DefaultBranchIri, ""));
    }
}
