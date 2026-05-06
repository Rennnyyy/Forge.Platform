using System.Reflection;
using System.Text.Json.Nodes;
using Forge.Aspects;
using Forge.Entity;
using Forge.Execution;
using Forge.Execution.Http;
using Forge.Operations;
using Forge.Operations.Http.DependencyInjection;
using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Operations.Http;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> that register REST operation
/// endpoints for all <see cref="OperationEndpointsAttribute"/>-annotated entities.
/// See Operations.Http ADR-0001.
/// </summary>
public static class OperationEndpointsEndpointRouteBuilderExtensions
{
    private static readonly MethodInfo RegisterEndpointsForMethod =
        typeof(OperationEndpointsEndpointRouteBuilderExtensions)
            .GetMethod(nameof(RegisterEndpointsFor), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(RegisterEndpointsFor)} from " +
            nameof(OperationEndpointsEndpointRouteBuilderExtensions));

    /// <summary>
    /// Discovers all <see cref="OperationEndpointDescriptor"/> registrations and maps a
    /// five-verb REST endpoint set per entity:
    /// <list type="table">
    ///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
    ///   <item><term>POST   api/entities/{path}</term><description>Create</description></item>
    ///   <item><term>GET    api/entities/{path}</term><description>List all / Read one (?iri=)</description></item>
    ///   <item><term>PUT    api/entities/{path}?iri=…</term><description>Update</description></item>
    ///   <item><term>DELETE api/entities/{path}?iri=…</term><description>Delete</description></item>
    /// </list>
    /// </summary>
    public static IEndpointRouteBuilder MapOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Create the operation-specific provider directly so it always reads from
        // X-Forge-Operation-AspectIri, even when Capability.Http has already registered
        // a different IExecutionAspectIriProvider in the shared DI slot. See ADR-0002.
        IExecutionAspectIriProvider opProvider =
            new HeaderExecutionAspectIriProvider(
                OperationEndpointsHttpServiceCollectionExtensions.AspectIriHeader);

        var descriptors = app.ServiceProvider
            .GetServices<OperationEndpointDescriptor>();

        foreach (var desc in descriptors)
        {
            RegisterEndpointsForMethod
                .MakeGenericMethod(desc.EntityType)
                .Invoke(null, [app, desc, opProvider]);
        }

        return app;
    }

    private static void RegisterEndpointsFor<T>(
        IEndpointRouteBuilder app,
        OperationEndpointDescriptor desc,
        IExecutionAspectIriProvider opProvider)
        where T : class, IEntity
    {
        var path = $"api/entities/{desc.Path}";

        // ── POST api/entities/{path} — Create ────────────────────────────────
        app.MapPost(path, async (
            HttpContext ctx,
            JsonObject body,
            ITransactionalEntityStore store) =>
        {
            using var _ = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                var entity = OperationEntityBinder.CreateFromJson<T>(body);

                await using var tx = EntityOperations.BeginTransaction();
                tx.Create(entity, aspectIri);
                await tx.CommitAsync();

                return Results.Ok(new OperationCreatedResponse(entity.Iri));
            });
        });

        // ── GET api/entities/{path}          — List  ─────────────────────────
        // ── GET api/entities/{path}?iri=…    — Read  ─────────────────────────
        app.MapGet(path, async (
            HttpContext ctx,
            string? iri,
            IEntityStore store) =>
        {
            using var _ = EntityOperations.Use(store);

            if (!string.IsNullOrEmpty(iri))
            {
                var entity = await EntityOperations.ReadAsync<T>(iri);
                if (entity is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No entity with IRI '{iri}' was found."));
                return Results.Ok(entity);
            }

            var items = new List<T>();
            await foreach (var item in EntityOperations.ListAsync<T>())
                items.Add(item);
            return Results.Ok(new OperationListResponse<T>(items));
        });

        // ── PUT api/entities/{path}?iri=… — Update ───────────────────────────
        app.MapPut(path, async (
            HttpContext ctx,
            string iri,
            JsonObject body,
            ITransactionalEntityStore store) =>
        {
            using var _ = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                var (entity, error) = OperationEntityBinder.UpdateFromJson<T>(iri, body);
                if (error is not null)
                    return Results.BadRequest(error);

                await using var tx = EntityOperations.BeginTransaction();
                tx.Update(entity!, aspectIri);
                await tx.CommitAsync();

                return Results.Ok(new OperationUpdatedResponse(entity!.Iri));
            });
        });

        // ── DELETE api/entities/{path}?iri=… — Delete ────────────────────────
        app.MapDelete(path, async (
            HttpContext ctx,
            string iri,
            ITransactionalEntityStore store) =>
        {
            using var _ = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                await using var tx = EntityOperations.BeginTransaction();
                tx.Delete(iri, aspectIri);
                await tx.CommitAsync();

                return Results.Ok(new OperationDeletedResponse());
            });
        });
    }
}
