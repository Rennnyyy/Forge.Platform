using Forge.Entity;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Validation;
using Forge.Validation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
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

// ─────────────────────────────────────────────────────────────────────────────
// Test set 4 — GuardedTransactionalStore: null-argument checks
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GuardedTransactionalStoreArgumentTests
{
    [Fact]
    public void Constructor_throws_for_null_inner_store()
    {
        Should.Throw<ArgumentNullException>(() =>
            new GuardedTransactionalStore(null!, AllowAllOperationGuard.Instance));
    }

    [Fact]
    public void Constructor_throws_for_null_guard()
    {
        var inner = Substitute.For<ITransactionalEntityStore>();
        Should.Throw<ArgumentNullException>(() =>
            new GuardedTransactionalStore(inner, null!));
    }

    [Fact]
    public async Task ExecuteTransactionAsync_throws_for_null_operations()
    {
        var inner = Substitute.For<ITransactionalEntityStore>();
        var store = new GuardedTransactionalStore(inner, AllowAllOperationGuard.Instance);
        await Should.ThrowAsync<ArgumentNullException>(
            () => store.ExecuteTransactionAsync(null!).AsTask());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test set 5 — GuardedTransactionalStore: QueryByTypeAsync authorization
// ─────────────────────────────────────────────────────────────────────────────

[Collection("EntityOptions")]
public sealed class GuardedTransactionalStoreQueryByTypeAsyncTests : IClassFixture<EntityOptionsFixture>
{
    private static Artist MakeArtist(string name = "Stream Test", string country = "us")
        => new() { Name = name, Country = country };

    private static async IAsyncEnumerable<T> EmptyStream<T>()
    {
        yield break;
    }

    // ── Test 5.1 — guard is called before inner ──────────────────────────────

    [Fact]
    public async Task QueryByTypeAsync_calls_guard_before_inner()
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
        mockInner
            .QueryByTypeAsync<Artist>(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("inner");
                return EmptyStream<Artist>();
            });

        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        await foreach (var _ in guarded.QueryByTypeAsync<Artist>()) { }

        callOrder.ShouldBe(["guard", "inner"]);
    }

    // ── Test 5.2 — guard receives NoOp aspect token ──────────────────────────

    [Fact]
    public async Task QueryByTypeAsync_passes_noop_aspect_token_to_guard()
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
        mockInner
            .QueryByTypeAsync<Artist>(Arg.Any<CancellationToken>())
            .Returns(EmptyStream<Artist>());

        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        await foreach (var _ in guarded.QueryByTypeAsync<Artist>()) { }

        capturedAspect.ShouldBe(Aspect.NoOp.Name);
    }

    // ── Test 5.3 — guard receives current agent token ────────────────────────

    [Fact]
    public async Task QueryByTypeAsync_passes_current_agent_token_to_guard()
    {
        string? capturedToken = null;

        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeQueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.ArgAt<string>(0);
                return ValueTask.CompletedTask;
            });

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        mockInner
            .QueryByTypeAsync<Artist>(Arg.Any<CancellationToken>())
            .Returns(EmptyStream<Artist>());

        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        using (ValidationContext.Use("stream-agent"))
        {
            await foreach (var _ in guarded.QueryByTypeAsync<Artist>()) { }
        }

        capturedToken.ShouldBe("stream-agent");
    }

    // ── Test 5.4 — denying guard prevents inner from being enumerated ─────────

    [Fact]
    public async Task QueryByTypeAsync_does_not_enumerate_inner_when_guard_throws()
    {
        var mockGuard = Substitute.For<IOperationGuard>();
        mockGuard
            .AuthorizeQueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new UnauthorizedAccessException("stream denied")));

        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);

        await Should.ThrowAsync<UnauthorizedAccessException>(async () =>
        {
            await foreach (var _ in guarded.QueryByTypeAsync<Artist>()) { }
        });

        mockInner.DidNotReceive().QueryByTypeAsync<Artist>(Arg.Any<CancellationToken>());
    }

    // ── Test 5.5 — end-to-end: allow-all guard streams persisted entities ─────

    [Fact]
    public async Task AllowAll_guard_lets_entities_stream_from_inner_store()
    {
        var registry = new RdfMapperRegistry();
        var options = new EntityRepositoryOptions();
        var inner = new InMemoryEntityStore(registry, Microsoft.Extensions.Options.Options.Create(options));
        var guarded = new GuardedTransactionalStore(inner, AllowAllOperationGuard.Instance);

        var a1 = MakeArtist("Stream Artist One", "us");
        var a2 = MakeArtist("Stream Artist Two", "uk");
        await guarded.ExecuteTransactionAsync([new CreateOperation<Artist>(a1), new CreateOperation<Artist>(a2)]);

        var names = new List<string>();
        await foreach (var a in guarded.QueryByTypeAsync<Artist>())
            names.Add(a.Name);

        names.ShouldContain("Stream Artist One");
        names.ShouldContain("Stream Artist Two");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test set 6 & 7 — GuardedTransactionalStore: direct delegation methods and
//                  explicit interface implementations
// ─────────────────────────────────────────────────────────────────────────────

[Collection("EntityOptions")]
public sealed class GuardedTransactionalStoreDelegationTests : IClassFixture<EntityOptionsFixture>
{
    private static (ITransactionalEntityStore mockInner, IOperationGuard mockGuard, GuardedTransactionalStore guarded)
        BuildMocks()
    {
        var mockInner = Substitute.For<ITransactionalEntityStore>();
        var mockGuard = Substitute.For<IOperationGuard>();
        var guarded = new GuardedTransactionalStore(mockInner, mockGuard);
        return (mockInner, mockGuard, guarded);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
            yield return item;
    }

    // ── Test 6.1 — SaveAsync delegates; guard not called ─────────────────────

    [Fact]
    public async Task SaveAsync_delegates_to_inner_without_calling_guard()
    {
        var (mockInner, mockGuard, guarded) = BuildMocks();
        var artist = new Artist { Name = "Delegated Save", Country = "us" };

        await guarded.SaveAsync(artist);

        await mockInner.Received(1).SaveAsync(
            Arg.Any<Artist>(), Arg.Any<WriteMode>(), Arg.Any<CancellationToken>());
        await mockGuard.DidNotReceive().AuthorizeTransactionAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>());
        await mockGuard.DidNotReceive().AuthorizeQueryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Test 6.2 — DeleteAsync delegates; guard not called ───────────────────

    [Fact]
    public async Task DeleteAsync_delegates_to_inner_without_calling_guard()
    {
        var (mockInner, mockGuard, guarded) = BuildMocks();
        const string iri = "https://forge-it.net/artists/to-delete";

        await guarded.DeleteAsync(iri);

        await mockInner.Received(1).DeleteAsync(iri, Arg.Any<CancellationToken>());
        await mockGuard.DidNotReceive().AuthorizeTransactionAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<TransactionOperation>>(), Arg.Any<CancellationToken>());
        await mockGuard.DidNotReceive().AuthorizeQueryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Test 6.3 — NamedGraph reads from inner ───────────────────────────────

    [Fact]
    public void NamedGraph_returns_value_from_inner()
    {
        var (mockInner, _, guarded) = BuildMocks();
        mockInner.NamedGraph.Returns("https://forge-it.net/graphs/named");

        guarded.NamedGraph.ShouldBe("https://forge-it.net/graphs/named");
    }

    [Fact]
    public void NamedGraph_returns_null_when_inner_graph_is_null()
    {
        var (mockInner, _, guarded) = BuildMocks();
        mockInner.NamedGraph.Returns((string?)null);

        guarded.NamedGraph.ShouldBeNull();
    }

    // ── Test 6.4 — DisposeAsync delegates to inner ───────────────────────────

    [Fact]
    public async Task DisposeAsync_delegates_to_inner()
    {
        var (mockInner, _, guarded) = BuildMocks();

        await guarded.DisposeAsync();

        await mockInner.Received(1).DisposeAsync();
    }

    // ── Test 7.1 — IEntityLoader.LoadAsync routes through the guard ──────────

    [Fact]
    public async Task IEntityLoader_LoadAsync_routes_through_guard_authorization()
    {
        var (mockInner, mockGuard, guarded) = BuildMocks();
        mockGuard
            .AuthorizeQueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        mockInner
            .LoadAsync<Artist>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Artist?>(null));

        IEntityLoader loader = guarded;
        var result = await loader.LoadAsync<Artist>("https://forge-it.net/artists/explicit-iface");

        result.ShouldBeNull();
        await mockGuard.Received(1).AuthorizeQueryAsync(
            Arg.Any<string>(), Aspect.NoOp.Name, Arg.Any<CancellationToken>());
        await mockInner.Received(1).LoadAsync<Artist>(
            "https://forge-it.net/artists/explicit-iface", Arg.Any<CancellationToken>());
    }

    // ── Test 7.2 — ICollectionLoader.LoadCollectionIrisAsync delegates directly (no guard) ─

    [Fact]
    public async Task ICollectionLoader_LoadCollectionIrisAsync_delegates_without_guard()
    {
        var (mockInner, mockGuard, guarded) = BuildMocks();
        var expectedIris = new[] { "https://forge-it.net/artists/1", "https://forge-it.net/artists/2" };
        ((ICollectionLoader)mockInner)
            .LoadCollectionIrisAsync<Artist>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(expectedIris));

        ICollectionLoader collectionLoader = guarded;
        var iris = new List<string>();
        await foreach (var iri in collectionLoader.LoadCollectionIrisAsync<Artist>(
            "https://forge-it.net/artists/owner", "hasAlbum"))
            iris.Add(iri);

        iris.ShouldBe(expectedIris, ignoreOrder: false);
        await mockGuard.DidNotReceive().AuthorizeQueryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test set 8 — AddForgeValidation DI extension
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AddForgeValidationTests
{
    // Minimal concrete store used to exercise the ImplementationType DI path.
    private sealed class NoOpTransactionalStore : ITransactionalEntityStore
    {
        public string? NamedGraph => null;

        public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
            where T : class, IEntity
            => ValueTask.FromResult<T?>(null);

        public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
            CancellationToken cancellationToken = default)
            where T : class, IEntity
            => ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
            where T : class, IEntity
            => EmptyEntityStream<T>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask ExecuteTransactionAsync(
            IReadOnlyList<TransactionOperation> operations,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
            string ownerIri, string predicate, CancellationToken cancellationToken)
            => EmptyStringStream();

        private static async IAsyncEnumerable<T> EmptyEntityStream<T>() { yield break; }
        private static async IAsyncEnumerable<string> EmptyStringStream() { yield break; }
    }

    // ── Test 8.1 — null services throws ──────────────────────────────────────

    [Fact]
    public void AddForgeValidation_throws_for_null_services()
    {
        Should.Throw<ArgumentNullException>(() =>
            ValidationServiceCollectionExtensions.AddForgeValidation(null!));
    }

    // ── Test 8.2 — no ITransactionalEntityStore registered → no-op ───────────

    [Fact]
    public void AddForgeValidation_is_no_op_when_no_store_is_registered()
    {
        var services = new ServiceCollection();
        var countBefore = services.Count;

        services.AddForgeValidation();

        services.Count.ShouldBe(countBefore);
    }

    // ── Test 8.3 — returns same collection (chainability) ────────────────────

    [Fact]
    public void AddForgeValidation_returns_same_service_collection()
    {
        var services = new ServiceCollection();
        var mock = Substitute.For<ITransactionalEntityStore>();
        services.AddSingleton<ITransactionalEntityStore>(mock);

        var result = services.AddForgeValidation();

        result.ShouldBeSameAs(services);
    }

    // ── Test 8.4 — instance descriptor + null guard → AllowAll ───────────────

    [Fact]
    public void AddForgeValidation_with_instance_descriptor_and_null_guard_wraps_store()
    {
        var services = new ServiceCollection();
        var mockStore = Substitute.For<ITransactionalEntityStore>();
        services.AddSingleton<ITransactionalEntityStore>(mockStore);

        services.AddForgeValidation(guard: null);

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<ITransactionalEntityStore>();
        resolved.ShouldBeOfType<GuardedTransactionalStore>();
    }

    // ── Test 8.5 — instance descriptor + explicit guard ──────────────────────

    [Fact]
    public void AddForgeValidation_with_instance_descriptor_and_explicit_guard_wires_that_guard()
    {
        var services = new ServiceCollection();
        var mockStore = Substitute.For<ITransactionalEntityStore>();
        var explicitGuard = Substitute.For<IOperationGuard>();
        services.AddSingleton<ITransactionalEntityStore>(mockStore);

        services.AddForgeValidation(guard: explicitGuard);

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<ITransactionalEntityStore>();
        resolved.ShouldBeOfType<GuardedTransactionalStore>();
    }

    // ── Test 8.6 — factory descriptor ────────────────────────────────────────

    [Fact]
    public void AddForgeValidation_with_factory_descriptor_wraps_in_guarded_store()
    {
        var services = new ServiceCollection();
        var mockStore = Substitute.For<ITransactionalEntityStore>();
        services.AddSingleton<ITransactionalEntityStore>(_ => mockStore);

        services.AddForgeValidation();

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<ITransactionalEntityStore>();
        resolved.ShouldBeOfType<GuardedTransactionalStore>();
    }

    // ── Test 8.7 — type descriptor (ActivatorUtilities.CreateInstance path) ──

    [Fact]
    public void AddForgeValidation_with_type_descriptor_wraps_in_guarded_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITransactionalEntityStore, NoOpTransactionalStore>();

        services.AddForgeValidation();

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<ITransactionalEntityStore>();
        resolved.ShouldBeOfType<GuardedTransactionalStore>();
    }
}
