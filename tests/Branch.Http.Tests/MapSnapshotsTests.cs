using System.Net;
using System.Net.Http.Json;
using Forge.Branch;
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
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Forge.Branch.Http.Tests;

/// <summary>
/// Integration tests for <see cref="BranchEndpointRouteBuilderExtensions.MapSnapshots"/>.
///
/// Uses a real in-process <see cref="TestServer"/>. Stores are hand-written test doubles;
/// <see cref="BranchSeedingService"/> is a real instance backed by those doubles.
/// </summary>
public sealed class MapSnapshotsTests
{
    private const string ManagementStoreKey = "forge.branch.management";
    private const string SnapshotPath = "/api/snapshots";
    private const string BranchPath = "/api/branches";

    // ─── Test helpers ─────────────────────────────────────────────────────────

    private static Snapshot MakeSnapshot(string name = "v1.0.0",
        int? major = 1, int? minor = 0, int? patch = 0) =>
        new()
        {
            Name = name,
            SnapshotAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SemVerMajor = major,
            SemVerMinor = minor,
            SemVerPatch = patch,
        };

    private static CreateSnapshotRequest ValidRequest(string name = "v1.0.0") =>
        new(
            Name: name,
            Description: null,
            SnapshotAt: null,
            SourceGraphIri: "https://forge-it.net/branches/main",
            EntityIris: ["https://forge-it.net/entities/a"],
            SemVerMajor: 1,
            SemVerMinor: 0,
            SemVerPatch: 0);

    // ─── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// In-memory management store test double. Configurable snapshots returned
    /// by QueryByTypeAsync and LoadAsync.
    /// </summary>
    private sealed class ManagementStoreDouble : ITransactionalEntityStore
    {
        public List<Snapshot> Snapshots { get; } = new();
        public bool ExecuteTransactionCalled { get; private set; }
        public Exception? ThrowOnExecute { get; set; }
        public string? NamedGraph => null;

        public ValueTask ExecuteTransactionAsync(
            IReadOnlyList<TransactionOperation> operations,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnExecute is not null) throw ThrowOnExecute;
            ExecuteTransactionCalled = true;
            return default;
        }

        public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
            where T : class, IEntity
        {
            if (typeof(T) == typeof(Snapshot))
            {
                var s = Snapshots.FirstOrDefault(x => x.Iri == iri);
                return new ValueTask<T?>((T?)(object?)s);
            }
            return new((T?)null);
        }

        public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
            where T : class, IEntity
        {
            if (typeof(T) == typeof(Snapshot))
                return (IAsyncEnumerable<T>)Snapshots.ToAsyncEnumerable();
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

    /// <summary>
    /// Data store test double — records executions and optionally throws on seed.
    /// </summary>
    private sealed class DataStoreDouble : ITransactionalEntityStore
    {
        public bool ExecuteTransactionCalled { get; private set; }
        public Exception? ThrowOnExecute { get; set; }
        public string? NamedGraph => null;

        public ValueTask ExecuteTransactionAsync(
            IReadOnlyList<TransactionOperation> operations,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnExecute is not null) throw ThrowOnExecute;
            ExecuteTransactionCalled = true;
            return default;
        }

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

    /// <summary>Tracks calls to <see cref="ISnapshotFrozenSetInvalidator"/>.</summary>
    private sealed class InvalidatorDouble : ISnapshotFrozenSetInvalidator
    {
        public int CallCount { get; private set; }

        public ValueTask InvalidateFrozenSetAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return default;
        }
    }

    // ─── BuildAsync ───────────────────────────────────────────────────────────

    private static async Task<(
        WebApplication app,
        ManagementStoreDouble managementStore,
        DataStoreDouble dataStore,
        InvalidatorDouble invalidator)>
        BuildAsync(
            ManagementStoreDouble? management = null,
            DataStoreDouble? data = null)
    {
        var managementStore = management ?? new ManagementStoreDouble();
        var dataStore = data ?? new DataStoreDouble();
        var invalidator = new InvalidatorDouble();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddKeyedSingleton<ITransactionalEntityStore>(ManagementStoreKey, managementStore);
        builder.Services.AddKeyedSingleton<IEntityStore>(ManagementStoreKey, (_, _) => managementStore);
        builder.Services.AddKeyedSingleton<ISnapshotFrozenSetInvalidator>(ManagementStoreKey, (_, _) => invalidator);
        builder.Services.AddSingleton<ITransactionalEntityStore>(dataStore);
        builder.Services.AddScoped<BranchSeedingService>();

        var app = builder.Build();
        app.MapBranches();
        app.MapSnapshots();
        await app.StartAsync();

        return (app, managementStore, dataStore, invalidator);
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST /api/snapshots
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_snapshot_returns_201_Created_on_success()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, ValidRequest());
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Post_snapshot_response_body_contains_IRI()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, ValidRequest("v2.0.0"));
            var body = await response.Content.ReadFromJsonAsync<OperationCreatedResponse>();

            body.ShouldNotBeNull();
            body!.Iri.ShouldContain("v2.0.0");
        }
    }

    [Fact]
    public async Task Post_snapshot_returns_400_when_Name_is_empty()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var req = ValidRequest() with { Name = "" };
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, req);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Post_snapshot_returns_400_when_EntityIris_is_empty()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var req = ValidRequest() with { EntityIris = [] };
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, req);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Post_snapshot_returns_400_when_SourceGraphIri_is_empty()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var req = ValidRequest() with { SourceGraphIri = "" };
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, req);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Post_snapshot_returns_409_Conflict_on_SemVer_conflict()
    {
        var mgmt = new ManagementStoreDouble();
        var existing = MakeSnapshot("v1.0.0", major: 1, minor: 0, patch: 0);
        existing.MaterializeIdentity();
        mgmt.Snapshots.Add(existing);

        var (app, _, _, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, ValidRequest("v1.0.0-copy"));

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("SNAPSHOT_VERSION_CONFLICT");
        }
    }

    [Fact]
    public async Task Post_snapshot_returns_422_when_seed_entities_missing()
    {
        var data = new DataStoreDouble
        {
            ThrowOnExecute = new SeedOperationMissingEntityException(
                "https://forge-it.net/branches/main",
                ["https://forge-it.net/entities/a"]),
        };

        var (app, _, _, _) = await BuildAsync(data: data);
        await using (app)
        {
            var response = await app.GetTestClient().PostAsJsonAsync(SnapshotPath, ValidRequest());

            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("SEED_MISSING_ENTITIES");
        }
    }

    [Fact]
    public async Task Post_snapshot_calls_InvalidateFrozenSet_on_success()
    {
        var (app, _, _, invalidator) = await BuildAsync();
        await using (app)
        {
            await app.GetTestClient().PostAsJsonAsync(SnapshotPath, ValidRequest());
            invalidator.CallCount.ShouldBe(1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // DELETE /api/snapshots?iri=…
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_snapshot_returns_200_OK_on_success()
    {
        var mgmt = new ManagementStoreDouble();
        var snapshot = MakeSnapshot("v1.0.0");
        snapshot.MaterializeIdentity();
        mgmt.Snapshots.Add(snapshot);

        var (app, _, _, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().DeleteAsync(
                $"{SnapshotPath}?iri={Uri.EscapeDataString(snapshot.Iri)}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Delete_snapshot_returns_404_when_not_found()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().DeleteAsync(
                $"{SnapshotPath}?iri=https://forge-it.net/snapshots/does-not-exist");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("NOT_FOUND");
        }
    }

    [Fact]
    public async Task Delete_snapshot_calls_InvalidateFrozenSet_on_success()
    {
        var mgmt = new ManagementStoreDouble();
        var snapshot = MakeSnapshot("v2.0.0");
        snapshot.MaterializeIdentity();
        mgmt.Snapshots.Add(snapshot);

        var (app, _, _, invalidator) = await BuildAsync(management: mgmt);
        await using (app)
        {
            await app.GetTestClient().DeleteAsync(
                $"{SnapshotPath}?iri={Uri.EscapeDataString(snapshot.Iri)}");
            invalidator.CallCount.ShouldBe(1);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/snapshots
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_snapshots_returns_200_with_snapshot_list()
    {
        var mgmt = new ManagementStoreDouble();
        var s1 = MakeSnapshot("v1.0.0");
        s1.MaterializeIdentity();
        var s2 = MakeSnapshot("v2.0.0", major: 2, minor: 0, patch: 0);
        s2.MaterializeIdentity();
        mgmt.Snapshots.AddRange([s1, s2]);

        var (app, _, _, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(SnapshotPath);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperationListResponse<Snapshot>>();
            body.ShouldNotBeNull();
            body!.Items.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Get_snapshots_returns_empty_list_when_none_exist()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(SnapshotPath);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperationListResponse<Snapshot>>();
            body!.Items.ShouldBeEmpty();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/snapshots?iri=…
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_snapshots_iri_returns_200_with_matching_snapshot()
    {
        var mgmt = new ManagementStoreDouble();
        var snapshot = MakeSnapshot("v1.0.0");
        snapshot.MaterializeIdentity();
        mgmt.Snapshots.Add(snapshot);

        var (app, _, _, _) = await BuildAsync(management: mgmt);
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(
                $"{SnapshotPath}?iri={Uri.EscapeDataString(snapshot.Iri)}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Get_snapshots_iri_returns_404_when_not_found()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var nonExistent = new Snapshot { Name = "v9.9.9" };
            nonExistent.MaterializeIdentity();
            var response = await app.GetTestClient().GetAsync(
                $"{SnapshotPath}?iri={Uri.EscapeDataString(nonExistent.Iri)}");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("NOT_FOUND");
        }
    }

    [Fact]
    public async Task Get_snapshots_iri_returns_400_for_non_absolute_iri()
    {
        var (app, _, _, _) = await BuildAsync();
        await using (app)
        {
            var response = await app.GetTestClient().GetAsync(
                $"{SnapshotPath}?iri=not-an-absolute-iri");

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<ExecutionError>();
            body!.Code.ShouldBe("INVALID_IRI");
        }
    }
}
