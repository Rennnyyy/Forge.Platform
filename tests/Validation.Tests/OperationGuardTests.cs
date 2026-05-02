using Forge.Entity;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Validation;
using NSubstitute;
using Shouldly;

namespace Forge.Validation.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Test set 1 — AllowAllOperationGuard
// ─────────────────────────────────────────────────────────────────────────────

[Collection("EntityOptions")]
public sealed class AllowAllOperationGuardTests : IClassFixture<EntityOptionsFixture>
{
    [Fact]
    public async Task AuthorizeTransactionAsync_always_completes_without_throw()
    {
        var guard = AllowAllOperationGuard.Instance;
        var artist = new Artist { Name = "Test", Country = "us" };
        var ops = new List<TransactionOperation> { new CreateOperation<Artist>(artist) };

        await guard.AuthorizeTransactionAsync("any-agent", ops).AsTask().ShouldNotThrowAsync();
    }

    [Fact]
    public async Task AuthorizeQueryAsync_always_completes_without_throw()
    {
        var guard = AllowAllOperationGuard.Instance;

        await guard.AuthorizeQueryAsync("any-agent", "any-aspect").AsTask().ShouldNotThrowAsync();
    }

    [Fact]
    public async Task AuthorizeTransactionAsync_completes_with_empty_operations_list()
    {
        var guard = AllowAllOperationGuard.Instance;

        await guard.AuthorizeTransactionAsync(string.Empty, []).AsTask().ShouldNotThrowAsync();
    }

    [Fact]
    public void Instance_is_singleton()
    {
        AllowAllOperationGuard.Instance.ShouldBeSameAs(AllowAllOperationGuard.Instance);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test set 2 — ValidationContext
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ValidationContextTests
{
    [Fact]
    public void CurrentAgentToken_is_null_when_no_scope_is_active()
    {
        // Run in isolation; AsyncLocal is flow-scoped so this is sufficient for a sync test.
        ValidationContext.CurrentAgentToken.ShouldBeNull();
    }

    [Fact]
    public void Use_binds_token_and_dispose_restores_null()
    {
        using (var scope = ValidationContext.Use("agent-abc"))
        {
            ValidationContext.CurrentAgentToken.ShouldBe("agent-abc");
        }

        ValidationContext.CurrentAgentToken.ShouldBeNull();
    }

    [Fact]
    public void Use_supports_nested_scopes()
    {
        using (ValidationContext.Use("outer"))
        {
            ValidationContext.CurrentAgentToken.ShouldBe("outer");

            using (ValidationContext.Use("inner"))
            {
                ValidationContext.CurrentAgentToken.ShouldBe("inner");
            }

            ValidationContext.CurrentAgentToken.ShouldBe("outer");
        }

        ValidationContext.CurrentAgentToken.ShouldBeNull();
    }

    [Fact]
    public void Use_throws_for_null_agentToken()
    {
        Should.Throw<ArgumentNullException>(() => ValidationContext.Use(null!));
    }

    [Fact]
    public void Use_throws_for_whitespace_agentToken()
    {
        Should.Throw<ArgumentException>(() => ValidationContext.Use("   "));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test set 3 — GuardedTransactionalStore
// ─────────────────────────────────────────────────────────────────────────────

[Collection("EntityOptions")]
public sealed class GuardedTransactionalStoreTests : IClassFixture<EntityOptionsFixture>
{
    private static (InMemoryEntityStore inner, GuardedTransactionalStore guarded, IOperationGuard guard)
        BuildStores(IOperationGuard? guard = null)
    {
        var registry = new RdfMapperRegistry();
        var options = new EntityRepositoryOptions();
        var inner = new InMemoryEntityStore(registry, Microsoft.Extensions.Options.Options.Create(options));
        var effectiveGuard = guard ?? AllowAllOperationGuard.Instance;
        var guarded = new GuardedTransactionalStore(inner, effectiveGuard);
        return (inner, guarded, effectiveGuard);
    }

    private static Artist MakeArtist(string name = "Guard Test", string country = "us")
        => new() { Name = name, Country = country };

    // ── Test 3.1 — guard is called before the inner store ───────────────────

    [Fact]
    public async Task ExecuteTransactionAsync_calls_guard_before_inner_store()
    {
        var callOrder = new List<string>();

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeTransactionAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add("guard");
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        mockInner
            .ExecuteTransactionAsync(Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add("inner");
                return ValueTask.CompletedTask;
            });

        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        var artist = MakeArtist();
        await guarded.ExecuteTransactionAsync([new CreateOperation<Artist>(artist)]);

        callOrder.ShouldBe(["guard", "inner"]);
    }

    // ── Test 3.2 — if guard throws, inner store is NOT called ───────────────

    [Fact]
    public async Task ExecuteTransactionAsync_does_not_call_inner_when_guard_throws()
    {
        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeTransactionAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new UnauthorizedAccessException("denied")));

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);

        var artist = MakeArtist();
        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => guarded.ExecuteTransactionAsync([new CreateOperation<Artist>(artist)]).AsTask());

        await mockInner.DidNotReceive()
            .ExecuteTransactionAsync(Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>());
    }

    // ── Test 3.3 — guard receives ALL operations ─────────────────────────────

    [Fact]
    public async Task ExecuteTransactionAsync_passes_all_operations_to_guard()
    {
        IReadOnlyList<TransactionOperation>? capturedOps = null;

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeTransactionAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOps = callInfo.Arg<IReadOnlyList<TransactionOperation>>();
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);

        var a1 = MakeArtist("A1", "us");
        var a2 = MakeArtist("A2", "uk");
        var ops = new List<TransactionOperation>
        {
            new CreateOperation<Artist>(a1),
            new CreateOperation<Artist>(a2),
        };

        await guarded.ExecuteTransactionAsync(ops);

        capturedOps.ShouldNotBeNull();
        capturedOps!.Count.ShouldBe(2);
    }

    // ── Test 3.4 — agentToken is passed from ValidationContext ───────────────

    [Fact]
    public async Task ExecuteTransactionAsync_passes_current_agent_token_to_guard()
    {
        string? capturedToken = null;

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeTransactionAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.Arg<string>();
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);

        using (ValidationContext.Use("my-agent-token"))
        {
            await guarded.ExecuteTransactionAsync([new CreateOperation<Artist>(MakeArtist())]);
        }

        capturedToken.ShouldBe("my-agent-token");
    }

    // ── Test 3.5 — empty agentToken when no ValidationContext scope ──────────

    [Fact]
    public async Task ExecuteTransactionAsync_passes_empty_string_when_no_validation_context()
    {
        string? capturedToken = null;

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeTransactionAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.Arg<string>();
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);

        await guarded.ExecuteTransactionAsync([new CreateOperation<Artist>(MakeArtist())]);

        capturedToken.ShouldBe(string.Empty);
    }

    // ── Test 3.6 — end-to-end: allow-all guard permits entity to be persisted ─

    [Fact]
    public async Task AllowAll_guard_lets_entity_reach_inner_store()
    {
        var (inner, guarded, _) = BuildStores();
        var artist = MakeArtist("Persisted Artist", "us");

        await guarded.ExecuteTransactionAsync([new CreateOperation<Artist>(artist)]);

        var loaded = await inner.LoadAsync<Artist>(artist.Iri);
        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Persisted Artist");
    }

    // ── Test 3.7 — AuthorizeQueryAsync is called before LoadAsync ────────────

    [Fact]
    public async Task LoadAsync_calls_guard_authorize_query_before_delegating()
    {
        var callOrder = new List<string>();

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeQueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("guard");
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        mockInner.LoadAsync<Artist>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("inner");
                return ValueTask.FromResult<Artist?>(null);
            });

        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        await guarded.LoadAsync<Artist>("https://forge-it.net/artists/test");

        callOrder.ShouldBe(["guard", "inner"]);
    }

    // ── Test 3.8 — AuthorizeQueryAsync is called with the NoOp aspect token ──

    [Fact]
    public async Task LoadAsync_passes_noop_aspect_token_to_guard()
    {
        string? capturedAspect = null;

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeQueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedAspect = callInfo.ArgAt<string>(1);
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        mockInner.LoadAsync<Artist>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Artist?>(null));

        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        await guarded.LoadAsync<Artist>("https://forge-it.net/artists/test");

        capturedAspect.ShouldBe(Aspect.NoOp.Name);
    }

    // ── Test 3.9 — denying guard blocks LoadAsync ────────────────────────────

    [Fact]
    public async Task LoadAsync_does_not_call_inner_when_guard_throws()
    {
        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeQueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new UnauthorizedAccessException("read denied")));

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => guarded.LoadAsync<Artist>("https://forge-it.net/artists/test").AsTask());

        await mockInner.DidNotReceive().LoadAsync<Artist>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
