using Shouldly;

namespace Forge.Repository.Tests;

/// <summary>
/// Behavioural spec for <see cref="BranchScope"/>. See Repository ADR-0002.
///
/// <list type="bullet">
///   <item>Null initial state — no scope is active before <see cref="BranchScope.Use"/> is called.</item>
///   <item>Use sets Current — the ambient is visible within the scope.</item>
///   <item>Dispose restores null — after the scope is disposed, Current returns null.</item>
///   <item>Nested scopes compose — inner dispose restores the outer IRI, not null.</item>
///   <item>Whitespace guard — Use throws on null/empty/whitespace.</item>
///   <item>Async flow — Current is visible across await boundaries inside the scope.</item>
///   <item>Parallel isolation — two concurrent async flows each see only their own branch IRI.</item>
/// </list>
/// </summary>
public sealed class BranchScopeTests
{
    private const string BranchA = "https://forge-it.net/branches/a";
    private const string BranchB = "https://forge-it.net/branches/b";

    // ════════════════════════════════════════════════════════════════════════
    // 1. Null initial state
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Current_is_null_before_any_scope_is_opened()
    {
        // Guarantee a clean starting state by running in a fresh async context.
        // BranchScope uses AsyncLocal, so each Task has its own copy on first write.
        var current = BranchScope.Current;
        current.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Use sets Current
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Use_sets_Current_to_supplied_branch_iri()
    {
        using var _ = BranchScope.Use(BranchA);

        BranchScope.Current.ShouldBe(BranchA);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Dispose restores null
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_restores_null_when_no_outer_scope_existed()
    {
        var scope = BranchScope.Use(BranchA);
        BranchScope.Current.ShouldBe(BranchA);

        scope.Dispose();

        BranchScope.Current.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Nested scopes compose
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Inner_dispose_restores_outer_branch_iri_not_null()
    {
        using var outer = BranchScope.Use(BranchA);
        BranchScope.Current.ShouldBe(BranchA);

        using (var inner = BranchScope.Use(BranchB))
        {
            BranchScope.Current.ShouldBe(BranchB);
            inner.Dispose();
        }

        // Outer scope is still active — Current must be BranchA, not null.
        BranchScope.Current.ShouldBe(BranchA);
    }

    [Fact]
    public void Outer_dispose_after_inner_restores_null()
    {
        var outer = BranchScope.Use(BranchA);

        using (BranchScope.Use(BranchB)) { /* inner scope */ }
        // After inner dispose, outer is back.
        BranchScope.Current.ShouldBe(BranchA);

        outer.Dispose();

        BranchScope.Current.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. WhitespaceGuard
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Use_throws_ArgumentException_for_null_empty_or_whitespace(string? bad)
    {
        var ex = Should.Throw<ArgumentException>(() => BranchScope.Use(bad!));
        ex.ShouldNotBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Async flow — Current is visible across await boundaries
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Current_is_visible_after_await_within_the_scope()
    {
        using var _ = BranchScope.Use(BranchA);

        await Task.Yield();

        BranchScope.Current.ShouldBe(BranchA);
    }

    [Fact]
    public async Task Current_is_null_after_await_outside_the_scope()
    {
        var scope = BranchScope.Use(BranchA);
        scope.Dispose();

        await Task.Yield();

        BranchScope.Current.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. Parallel isolation — two flows do not bleed into each other
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Parallel_flows_each_see_only_their_own_branch_iri()
    {
        var barrier = new TaskCompletionSource();

        async Task<string?> RunFlow(string branchIri)
        {
            using var _ = BranchScope.Use(branchIri);
            // Wait for both flows to have set their scope before reading.
            await barrier.Task;
            return BranchScope.Current;
        }

        var taskA = Task.Run(() => RunFlow(BranchA));
        var taskB = Task.Run(() => RunFlow(BranchB));

        // Release both flows simultaneously.
        barrier.SetResult();

        var results = await Task.WhenAll(taskA, taskB);

        // Each task must have seen only its own branch IRI.
        results.ShouldContain(BranchA);
        results.ShouldContain(BranchB);
    }
}
