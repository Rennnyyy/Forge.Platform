using BranchEntity = Forge.Branch.Branch;
using Forge.Aspects.Abstractions;
using Forge.Branch;
using Forge.Execution;
using Forge.Execution.Http;
using Forge.Operations.Http;
using Forge.Operations.Http.DependencyInjection;
using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SnapshotEntity = Forge.Branch.Snapshot;

namespace Forge.Branch.Http;

// ── Request bodies ────────────────────────────────────────────────────────────

/// <summary>Request body for creating a branch.</summary>
/// <param name="Name">Human-readable slug, e.g. <c>"feature-x"</c>. Forms the branch IRI.</param>
/// <param name="Description">Optional description.</param>
internal sealed record CreateBranchRequest(string Name, string? Description);

// ── Snapshot request / response models ───────────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/snapshots</c>.
/// </summary>
/// <param name="Name">Slug that forms the snapshot IRI, e.g. <c>"v1.0.0"</c>.</param>
/// <param name="Description">Optional description.</param>
/// <param name="SnapshotAt">Logical freeze time. Defaults to <c>UtcNow</c> when omitted.</param>
/// <param name="SourceGraphIri">Named graph whose entity triples are copied into the new snapshot graph.</param>
/// <param name="EntityIris">Explicit list of entity IRIs to include from <paramref name="SourceGraphIri"/>.</param>
/// <param name="SemVerMajor">Optional semantic version major.</param>
/// <param name="SemVerMinor">Optional semantic version minor.</param>
/// <param name="SemVerPatch">Optional semantic version patch.</param>
/// <param name="SemVerPreRelease">Optional pre-release label, e.g. <c>"alpha.1"</c>.</param>
internal sealed record CreateSnapshotRequest(
    string Name,
    string? Description,
    DateTimeOffset? SnapshotAt,
    string SourceGraphIri,
    IReadOnlyList<string> EntityIris,
    int? SemVerMajor = null,
    int? SemVerMinor = null,
    int? SemVerPatch = null,
    string? SemVerPreRelease = null);

/// <summary>Request body for updating a branch. Only mutable fields are accepted.</summary>
/// <param name="Description">Updated description. Pass <see langword="null"/> to clear it.</param>
internal sealed record UpdateBranchRequest(string? Description);

// ── MapBranches() ─────────────────────────────────────────────────────────────

/// <summary>
/// Endpoint route builder extensions for the Branch HTTP layer.
/// See Branch.Http ADR-0001.
/// </summary>
public static class BranchEndpointRouteBuilderExtensions
{
    private const string ManagementStoreKey = "forge.branch.management";
    private const string BasePath = "api/branches";

    /// <summary>
    /// Maps five REST endpoints for Branch entity management, all targeting the
    /// management named graph via the keyed <c>"forge.branch.management"</c> store:
    /// <list type="table">
    ///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
    ///   <item><term>POST   api/branches</term><description>Create a new branch</description></item>
    ///   <item><term>GET    api/branches</term><description>List all branches</description></item>
    ///   <item><term>GET    api/branches?iri=…</term><description>Read a single branch</description></item>
    ///   <item><term>PUT    api/branches?iri=…</term><description>Update a branch</description></item>
    ///   <item><term>DELETE api/branches?iri=…</term><description>Delete branch + drop its named graph</description></item>
    /// </list>
    /// Requires <c>AddForgeBranch()</c> and <c>UseBranchScope()</c> to have been registered.
    /// </summary>
    public static IEndpointRouteBuilder MapBranches(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Created once at registration time — not per-request — mirroring MapOperations(). See ADR-0002.
        IExecutionAspectIriProvider aspectIriProvider =
            new HeaderExecutionAspectIriProvider(
                OperationEndpointsHttpServiceCollectionExtensions.AspectIriHeader);

        // ── POST api/branches — Create ────────────────────────────────────────
        app.MapPost(BasePath, async (
            HttpContext ctx,
            CreateBranchRequest body,
            [FromKeyedServices(ManagementStoreKey)] ITransactionalEntityStore managementStore,
            CancellationToken ct) =>
        {
            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                var aspectIri = await aspectIriProvider.GetAspectIriAsync(ctx, ct) ?? Aspect.NoOpIri;

                var branch = new BranchEntity
                {
                    Name = body.Name,
                    Description = body.Description,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                await using var tx = new EntityTransaction(managementStore);
                tx.Create(branch, aspectIri);
                await tx.CommitAsync(ct);

                return Results.Ok(new OperationCreatedResponse(branch.Iri));
            });
        });

        // ── GET api/branches               — List mutable branches ────────────
        // ── GET api/branches?iri=…         — Read a single Branch by IRI ─────
        app.MapGet(BasePath, async (
            string? iri,
            [FromKeyedServices(ManagementStoreKey)] IEntityStore managementStore,
            CancellationToken ct) =>
        {
            // ── ?iri=… — read a single branch ────────────────────────────────────
            var repo = new EntityRepository<BranchEntity>(managementStore);

            if (!string.IsNullOrEmpty(iri))
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                var branch = await repo.FindAsync(iri, ct);
                if (branch is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No branch with IRI '{iri}' was found."));

                return Results.Ok(branch);
            }

            // ── bare list — mutable branches only ────────────────────────────────
            var items = new List<BranchEntity>();
            await foreach (var b in repo.QueryAllAsync(ct))
                items.Add(b);

            return Results.Ok(new OperationListResponse<BranchEntity>(items));
        });

        // ── PUT api/branches?iri=… — Update ──────────────────────────────────
        app.MapPut(BasePath, async (
            HttpContext ctx,
            string iri,
            UpdateBranchRequest body,
            [FromKeyedServices(ManagementStoreKey)] ITransactionalEntityStore managementStore,
            CancellationToken ct) =>
        {
            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                var aspectIri = await aspectIriProvider.GetAspectIriAsync(ctx, ct) ?? Aspect.NoOpIri;

                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                var repo = new EntityRepository<BranchEntity>(managementStore);
                var branch = await repo.FindAsync(iri, ct);
                if (branch is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No branch with IRI '{iri}' was found."));

                branch.Description = body.Description;

                await using var tx = new EntityTransaction(managementStore);
                tx.Update(branch, aspectIri);
                await tx.CommitAsync(ct);

                return Results.Ok(new OperationUpdatedResponse(branch.Iri));
            });
        });

        // ── DELETE api/branches?iri=… — Delete ───────────────────────────────
        // Removes the Branch entity metadata from the management graph and drops the
        // branch-owned data graph so all branch data is cleaned up.
        // Blocked by BranchGuardedTransactionalStore when the target is the default branch
        // or the management graph itself; returns 422 in that case.
        // ExecutionEndpointHelper translates AspectViolationException → 422 ENTITY_SHACL_VIOLATION
        // (aspect enforcement on the management store — see root ADR-0019).
        app.MapDelete(BasePath, (
            HttpContext ctx,
            string iri,
            [FromKeyedServices(ManagementStoreKey)] ITransactionalEntityStore managementStore,
            ITransactionalEntityStore dataStore,
            CancellationToken ct) =>
        {
            return ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                var repo = new EntityRepository<BranchEntity>(managementStore);
                var branch = await repo.FindAsync(iri, ct);
                if (branch is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No branch with IRI '{iri}' was found."));

                var aspectIri = await aspectIriProvider.GetAspectIriAsync(ctx, ct) ?? Aspect.NoOpIri;

                try
                {
                    await using var tx = new EntityTransaction(managementStore);
                    tx.Delete<BranchEntity>(branch.Iri, aspectIri);
                    await tx.CommitAsync(ct);

                    using var _ = BranchScope.Use(branch.Iri);
                    await using var dataTx = new EntityTransaction(dataStore);
                    dataTx.DropGraph(branch.Iri);
                    await dataTx.CommitAsync(ct);
                }
                catch (BranchProtectionViolationException ex)
                {
                    return Results.UnprocessableEntity(
                        new ExecutionError("BRANCH_PROTECTED", ex.Message));
                }

                return Results.Ok(new OperationDeletedResponse());
            });
        });

        return app;
    }

    // ─────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Maps REST endpoints for Snapshot entity management:
    /// <list type="table">
    ///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
    ///   <item><term>POST   api/snapshots</term><description>Create and seed a new snapshot</description></item>
    ///   <item><term>GET    api/snapshots</term><description>List all snapshots</description></item>
    ///   <item><term>GET    api/snapshots?iri=…</term><description>Read a single snapshot by IRI</description></item>
    ///   <item><term>DELETE api/snapshots?iri=…</term><description>Delete snapshot + drop its named graph</description></item>
    /// </list>
    /// See Branch ADR-0002 and ADR-0003.
    /// </summary>
    public static IEndpointRouteBuilder MapSnapshots(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        const string SnapshotPath = "api/snapshots";

        // Created once at registration time — not per-request — mirroring MapOperations(). See ADR-0002.
        IExecutionAspectIriProvider aspectIriProvider =
            new HeaderExecutionAspectIriProvider(
                OperationEndpointsHttpServiceCollectionExtensions.AspectIriHeader);

        // ── POST api/snapshots — Create and seed ──────────────────────────────────
        app.MapPost(SnapshotPath, async (
            HttpContext ctx,
            CreateSnapshotRequest body,
            BranchSeedingService seedingService,
            CancellationToken ct) =>
        {
            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.BadRequest(new ExecutionError("INVALID_NAME",
                        "Name must not be null or whitespace."));

                if (body.EntityIris is null || body.EntityIris.Count == 0)
                    return Results.BadRequest(new ExecutionError("INVALID_ENTITY_IRIS",
                        "EntityIris must contain at least one IRI."));

                if (string.IsNullOrWhiteSpace(body.SourceGraphIri))
                    return Results.BadRequest(new ExecutionError("INVALID_SOURCE_GRAPH_IRI",
                        "SourceGraphIri must not be null or whitespace."));

                var aspectIri = await aspectIriProvider.GetAspectIriAsync(ctx, ct) ?? Aspect.NoOpIri;

                var snapshot = new SnapshotEntity
                {
                    Name = body.Name,
                    Description = body.Description,
                    CreatedAt = DateTimeOffset.UtcNow,
                    SnapshotAt = body.SnapshotAt ?? DateTimeOffset.UtcNow,
                    SemVerMajor = body.SemVerMajor,
                    SemVerMinor = body.SemVerMinor,
                    SemVerPatch = body.SemVerPatch,
                    SemVerPreRelease = body.SemVerPreRelease,
                };

                try
                {
                    await seedingService.CreateSnapshotAsync(
                        snapshot, body.SourceGraphIri, body.EntityIris, aspectIri, ct);
                }
                catch (SnapshotVersionConflictException ex)
                {
                    return Results.Conflict(
                        new ExecutionError("SNAPSHOT_VERSION_CONFLICT", ex.Message));
                }
                catch (SeedOperationMissingEntityException ex)
                {
                    return Results.UnprocessableEntity(
                        new ExecutionError("SEED_MISSING_ENTITIES", ex.Message));
                }

                return Results.Created($"/api/snapshots/{body.Name}",
                    new OperationCreatedResponse(snapshot.Iri));
            });
        });

        // ── GET api/snapshots       ───────────────── List all snapshots ────────────
        // ── GET api/snapshots?iri=… ──────────── Read a single snapshot by IRI ───
        app.MapGet(SnapshotPath, async (
            string? iri,
            [FromKeyedServices(ManagementStoreKey)] IEntityStore managementStore,
            CancellationToken ct) =>
        {
            var snapshotRepo = new EntityRepository<SnapshotEntity>(managementStore);

            // ── ?iri=… — read a single snapshot by IRI ─────────────────────
            if (!string.IsNullOrEmpty(iri))
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                var snapshot = await snapshotRepo.FindAsync(iri, ct);
                if (snapshot is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No snapshot with IRI '{iri}' was found."));

                return Results.Ok(snapshot);
            }

            // ── bare list — all snapshots ─────────────────────────────────
            var items = new List<SnapshotEntity>();
            await foreach (var s in snapshotRepo.QueryAllAsync(ct))
                items.Add(s);

            return Results.Ok(new OperationListResponse<SnapshotEntity>(items));
        });

        // ── DELETE api/snapshots?iri=… — Delete ──────────────────────────────────
        // Delegates to BranchSeedingService.DeleteSnapshotAsync which issues the paired
        // Delete+DropGraph and refreshes the frozen-set guard atomically.
        app.MapDelete(SnapshotPath, async (
            HttpContext ctx,
            string iri,
            BranchSeedingService seedingService,
            [FromKeyedServices(ManagementStoreKey)] IEntityStore managementStore,
            CancellationToken ct) =>
        {
            if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                return Results.BadRequest(new ExecutionError("INVALID_IRI",
                    $"The value '{iri}' is not a valid absolute IRI."));

            var repo = new EntityRepository<SnapshotEntity>(managementStore);
            var snapshot = await repo.FindAsync(iri, ct);
            if (snapshot is null)
                return Results.NotFound(new ExecutionError("NOT_FOUND",
                    $"No snapshot with IRI '{iri}' was found."));

            var aspectIri = await aspectIriProvider.GetAspectIriAsync(ctx, ct) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                await seedingService.DeleteSnapshotAsync(snapshot, aspectIri, ct);
                return Results.Ok(new OperationDeletedResponse());
            });
        });

        return app;
    }
}
