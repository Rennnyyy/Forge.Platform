using BranchEntity = Forge.Branch.Branch;
using Forge.Branch;
using Forge.Execution;
using Forge.Execution.Http;
using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Branch.Http;

// ── Request bodies ────────────────────────────────────────────────────────────

/// <summary>Request body for creating a branch.</summary>
/// <param name="Name">Human-readable slug, e.g. <c>"feature-x"</c>. Forms the branch IRI.</param>
/// <param name="Description">Optional description.</param>
internal sealed record CreateBranchRequest(string Name, string? Description);

/// <summary>Request body for updating a branch. Only mutable fields are accepted.</summary>
/// <param name="Description">Updated description. Pass <see langword="null"/> to clear it.</param>
internal sealed record UpdateBranchRequest(string? Description);

// ── Response types ────────────────────────────────────────────────────────────

/// <summary>Returned after a successful branch creation.</summary>
/// <param name="Iri">The IRI of the newly created branch (also its named graph IRI).</param>
public sealed record BranchCreatedResponse(string Iri);

/// <summary>Returned after a successful branch update.</summary>
/// <param name="Iri">The IRI of the updated branch.</param>
public sealed record BranchUpdatedResponse(string Iri);

/// <summary>Returned after a successful branch deletion.</summary>
public sealed record BranchDeletedResponse();

/// <summary>Returned by the list endpoint.</summary>
public sealed record BranchListResponse(IReadOnlyList<BranchEntity> Items);

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

        // ── POST api/branches — Create ────────────────────────────────────────
        app.MapPost(BasePath, async (
            CreateBranchRequest body,
            [FromKeyedServices(ManagementStoreKey)] ITransactionalEntityStore managementStore,
            CancellationToken ct) =>
        {
            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                var branch = new BranchEntity
                {
                    Name = body.Name,
                    Description = body.Description,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                await using var tx = new EntityTransaction(managementStore);
                tx.Create(branch);
                await tx.CommitAsync(ct);

                return Results.Ok(new BranchCreatedResponse(branch.Iri));
            });
        });

        // ── GET api/branches          — List  ─────────────────────────────────
        // ── GET api/branches?iri=…   — Read  ─────────────────────────────────
        app.MapGet(BasePath, async (
            string? iri,
            [FromKeyedServices(ManagementStoreKey)] IEntityStore managementStore,
            CancellationToken ct) =>
        {
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

            var items = new List<BranchEntity>();
            await foreach (var b in repo.QueryAllAsync(ct))
                items.Add(b);

            return Results.Ok(new BranchListResponse(items));
        });

        // ── PUT api/branches?iri=… — Update ──────────────────────────────────
        app.MapPut(BasePath, async (
            string iri,
            UpdateBranchRequest body,
            [FromKeyedServices(ManagementStoreKey)] ITransactionalEntityStore managementStore,
            CancellationToken ct) =>
        {
            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
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
                tx.Update(branch);
                await tx.CommitAsync(ct);

                return Results.Ok(new BranchUpdatedResponse(branch.Iri));
            });
        });

        // ── DELETE api/branches?iri=… — Delete ───────────────────────────────
        // Removes the Branch entity metadata from the management graph and drops the
        // branch-owned data graph so all branch data is cleaned up.
        // Blocked by BranchGuardedTransactionalStore when the target is the default branch
        // or the management graph itself; returns 422 in that case.
        app.MapDelete(BasePath, async (
            string iri,
            [FromKeyedServices(ManagementStoreKey)] ITransactionalEntityStore managementStore,
            ITransactionalEntityStore dataStore,
            CancellationToken ct) =>
        {
            if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                return Results.BadRequest(new ExecutionError("INVALID_IRI",
                    $"The value '{iri}' is not a valid absolute IRI."));

            var repo = new EntityRepository<BranchEntity>(managementStore);
            var branch = await repo.FindAsync(iri, ct);
            if (branch is null)
                return Results.NotFound(new ExecutionError("NOT_FOUND",
                    $"No branch with IRI '{iri}' was found."));

            try
            {
                await using var tx = new EntityTransaction(managementStore);
                tx.Delete(branch.Iri);
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

            return Results.Ok(new BranchDeletedResponse());
        });

        return app;
    }
}
