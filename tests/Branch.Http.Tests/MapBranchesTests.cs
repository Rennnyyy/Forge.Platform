using System.Net;
using System.Net.Http.Json;
using BranchEntity = Forge.Branch.Branch;
using Forge.Branch.Http;
using Forge.Entity;
using Forge.Execution;
using Forge.Operations.Http;
using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Branch.Http.Tests;

/// <summary>
/// Integration tests for <see cref="BranchEndpointRouteBuilderExtensions.MapBranches"/>.
///
/// Uses a real in-process <see cref="TestServer"/>. Stores are hand-written test doubles;
/// no real RDF backend is required.
/// </summary>
public sealed class MapBranchesTests
{
    private const string ManagementStoreKey = "forge.branch.management";
    private const string BranchPath = "/api/branches";

    // ─── Test helpers ─────────────────────────────────────────────────────────

    private static CreateBranchRequest ValidRequest(string name = "feature-x") =>
        new(Name: name, Description: null);

    private static BranchEntity MakeBranch(string name = "main")
    {
        var b = new BranchEntity { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        b.MaterializeIdentity();
        return b;
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// Management store test double. Configurable branches returned by
    /// QueryByTypeAsync and LoadAsync.
    /// </summary>
    private sealed class ManagementStoreDouble : ITransactionalEntityStore
    {
        public List<BranchEntity> Branches { get; } = new();
        public bool ExecuteTransactionCalled { get; private set; }
        public string? NamedGraph => null;

        public ValueTask ExecuteTransactionAsync(
            IReadOnlyList<TransactionOperation> operations,
            CancellationToken cancellationToken = default)
        {
            ExecuteTransactionCalled = true;
            return default;
        }

        public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
            where T : class, IEntity
        {
            if (typeof(T) == typeof(BranchEntity))
            {
                var b = Branches.FirstOrDefault(x => x.Iri == iri);
                return new ValueTask<T?>((T?)(object?)b);
            }
            return new((T?)null);
        }

        public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
            where T : class, IEntity
        {
            if (typeof(T) == typeof(BranchEntity))
                return (IAsyncEnumerable<T>)Branches.ToAsyncEnumerable();
            return AsyncEnumerable.Empty<T>();
        }

        public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
            CancellationToken cancellationToken = default) where T : class, IEntity => default;

        public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default) => default;

        public ValueTask DisposeAsync() => default;

        ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
            where T : class
            => LoadAsync<T>(iri, cancellationToken);

        IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
            string ownerIri, string predicate, CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<string>();
    }

    /// <summary>Data store test double — accepts any transaction without side effects.</summary>
    private sealed class DataStoreDouble : ITransactionalEntityStore
    {
        public string? NamedGraph => null;

        public ValueTask ExecuteTransactionAsync(
            IReadOnlyList<TransactionOperation> operations,
            CancellationToken cancellationToken = default) => default;

        public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
            where T : class, IEntity => new((T?)null);

        public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
            where T : class, IEntity => AsyncEnumerable.Empty<T>();

        public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
            CancellationToken cancellationToken = default) where T : class, IEntity => default;

        public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default) => default;

        public ValueTask DisposeAsync() => default;

        ValueTask<T?> IEntityLoader.LoadAsync<T>(string iri, CancellationToken cancellationToken)
            where T : class => new((T?)null);

        IAsyncEnumerable<string> ICollectionLoader.LoadCollectionIrisAsync<T>(
            string ownerIri, string predicate, CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<string>();
    }

    // ─── BuildAsync ───────────────────────────────────────────────────────────

    private static async Task<(WebApplication app, ManagementStoreDouble managementStore)> BuildAsync(
        ManagementStoreDouble? management = null)
    {
        var managementStore = management ?? new ManagementStoreDouble();
        var dataStore = new DataStoreDouble();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddKeyedSingleton<ITransactionalEntityStore>(ManagementStoreKey, managementStore);
        builder.Services.AddKeyedSingleton<IEntityStore>(ManagementStoreKey, (_, _) => managementStore);
        builder.Services.AddSingleton<ITransactionalEntityStore>(dataStore);

        var app = builder.Build();
        app.MapBranches();
        await app.StartAsync();

        return (app, managementStore);
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST /api/branches
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_branch_returns_200_on_success()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().PostAsJsonAsync(BranchPath, ValidRequest());
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Post_branch_response_body_contains_IRI()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().PostAsJsonAsync(BranchPath, ValidRequest("feature-y"));
            var body = await response.Content.ReadFromJsonAsync<OperationCreatedResponse>();

            body.ShouldNotBeNull();
            body!.Iri.ShouldContain("feature-y");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/branches  (list)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_branches_returns_200_with_empty_list_when_no_branches_exist()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(BranchPath);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<OperationListResponse<BranchEntity>>();
            body.ShouldNotBeNull();
            body!.Items.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Get_branches_returns_all_registered_branches()
    {
        var mgmt = new ManagementStoreDouble();
        mgmt.Branches.Add(MakeBranch("main"));
        mgmt.Branches.Add(MakeBranch("develop"));

        var (app, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(BranchPath);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<OperationListResponse<BranchEntity>>();
            body!.Items.Count.ShouldBe(2);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/branches?iri=…
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_branch_by_iri_returns_200_and_entity_when_found()
    {
        var mgmt = new ManagementStoreDouble();
        var branch = MakeBranch("main");
        mgmt.Branches.Add(branch);

        var (app, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync($"{BranchPath}?iri={Uri.EscapeDataString(branch.Iri)}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<BranchEntity>();
            body.ShouldNotBeNull();
            body!.Name.ShouldBe("main");
        }
    }

    [Fact]
    public async Task Get_branch_by_iri_returns_404_when_not_found()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(
                $"{BranchPath}?iri={Uri.EscapeDataString("https://forge-it.net/branches/ghost")}");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("NOT_FOUND");
        }
    }

    [Fact]
    public async Task Get_branch_by_iri_returns_400_for_invalid_IRI()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync($"{BranchPath}?iri=not-a-valid-iri");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("INVALID_IRI");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // PUT /api/branches?iri=…
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_branch_returns_200_on_success()
    {
        var mgmt = new ManagementStoreDouble();
        var branch = MakeBranch("main");
        mgmt.Branches.Add(branch);

        var (app, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().PutAsJsonAsync(
                $"{BranchPath}?iri={Uri.EscapeDataString(branch.Iri)}",
                new UpdateBranchRequest(Description: "updated"));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperationUpdatedResponse>();
            body!.Iri.ShouldBe(branch.Iri);
        }
    }

    [Fact]
    public async Task Put_branch_returns_404_when_not_found()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().PutAsJsonAsync(
                $"{BranchPath}?iri={Uri.EscapeDataString("https://forge-it.net/branches/ghost")}",
                new UpdateBranchRequest(Description: null));

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Put_branch_returns_400_for_invalid_IRI()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().PutAsJsonAsync(
                $"{BranchPath}?iri=not-a-valid-iri",
                new UpdateBranchRequest(Description: null));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("INVALID_IRI");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // DELETE /api/branches?iri=…
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_branch_returns_200_on_success()
    {
        var mgmt = new ManagementStoreDouble();
        var branch = MakeBranch("feature-x");
        mgmt.Branches.Add(branch);

        var (app, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().DeleteAsync(
                $"{BranchPath}?iri={Uri.EscapeDataString(branch.Iri)}");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Delete_branch_returns_404_when_not_found()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().DeleteAsync(
                $"{BranchPath}?iri={Uri.EscapeDataString("https://forge-it.net/branches/ghost")}");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Delete_branch_returns_400_for_invalid_IRI()
    {
        var (app, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().DeleteAsync(
                $"{BranchPath}?iri=not-a-valid-iri");

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("INVALID_IRI");
        }
    }
}
