using System.Reflection;
using System.Text.Json.Nodes;
using Forge.Aspects;
using Forge.Aspects.Abstractions;
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

    private static readonly MethodInfo RegisterEnumerationEndpointsForMethod =
        typeof(OperationEndpointsEndpointRouteBuilderExtensions)
            .GetMethod(nameof(RegisterEnumerationEndpointsFor), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(RegisterEnumerationEndpointsFor)} from " +
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
            var isEnumeration = desc.EntityType
                .GetCustomAttribute<EnumerationAttribute>() is not null;

            if (isEnumeration)
                RegisterEnumerationEndpointsForMethod
                    .MakeGenericMethod(desc.EntityType)
                    .Invoke(null, [app, desc]);
            else
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
            using var _ops = EntityOperations.Use(store);
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
            using var _ops = EntityOperations.Use(store);

            if (!string.IsNullOrEmpty(iri))
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

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
            using var _ops = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

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
            using var _ops = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                await using var tx = EntityOperations.BeginTransaction();
                tx.Delete(iri, aspectIri);
                await tx.CommitAsync();

                return Results.Ok(new OperationDeletedResponse());
            });
        });
    }

    /// <summary>
    /// Registers read-only GET (List + Read) endpoints for <c>[Enumeration]</c> entity types.
    /// Instances are served from the static <c>IReadOnlyList&lt;T&gt; All</c> property
    /// compiled into the assembly — no store access required.
    /// </summary>
    private static void RegisterEnumerationEndpointsFor<T>(
        IEndpointRouteBuilder app,
        OperationEndpointDescriptor desc)
        where T : class, IEntity
    {
        // Reflect the static `All` property once at registration time.
        var allProperty = typeof(T).GetProperty(
            "All",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        IReadOnlyList<T> GetAll() =>
            allProperty?.GetValue(null) as IReadOnlyList<T> ?? [];

        var path = $"api/entities/{desc.Path}";

        // ── GET api/entities/{path}          — List  ─────────────────────────
        // ── GET api/entities/{path}?iri=…    — Read  ─────────────────────────
        app.MapGet(path, (string? iri) =>
        {
            var all = GetAll();

            if (!string.IsNullOrEmpty(iri))
            {
                var match = all.FirstOrDefault(e => e.Iri == iri);
                if (match is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No {typeof(T).Name} with IRI '{iri}' exists. " +
                        $"Use GET api/entities/{desc.Path} to list valid IRIs."));
                return Results.Ok(match);
            }

            return Results.Ok(new OperationListResponse<T>([.. all]));
        });
    }
}
