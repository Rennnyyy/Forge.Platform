using System.Reflection;
using System.Text.Json.Nodes;
using Forge.Aspects.Abstractions;
using Forge.Entity;
using Forge.Execution;
using Forge.Execution.Http;
using Forge.ObjectStorage;
using Forge.Operations;
using Forge.Operations.Http;
using Forge.Operations.Http.DependencyInjection;
using Forge.Repository;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.ObjectStorage.Http;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> that register REST operation
/// endpoints for all <c>[ObjectBearing]</c>-annotated entities.
/// See <c>ObjectStorage.Http</c> ADR-0001.
/// </summary>
public static class ObjectOperationRouteBuilderExtensions
{
    private static readonly MethodInfo RegisterObjectEndpointsForMethod =
        typeof(ObjectOperationRouteBuilderExtensions)
            .GetMethod(nameof(RegisterObjectEndpointsFor), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(RegisterObjectEndpointsFor)} from " +
            nameof(ObjectOperationRouteBuilderExtensions));

    /// <summary>
    /// Discovers all <see cref="ObjectOperationDescriptor"/> registrations and maps a
    /// eight-route REST contract per entity — five metadata verbs at <c>api/entities/{path}</c>
    /// and three binary-channel verbs at <c>api/objects/{path}/content</c>:
    /// <list type="table">
    ///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
    ///   <item><term>POST   api/entities/{path}</term><description>Create metadata entity</description></item>
    ///   <item><term>GET    api/entities/{path}</term><description>List all metadata entities</description></item>
    ///   <item><term>GET    api/entities/{path}?iri=…</term><description>Read single metadata entity</description></item>
    ///   <item><term>PUT    api/entities/{path}?iri=…</term><description>Update metadata entity</description></item>
    ///   <item><term>DELETE api/entities/{path}?iri=…</term><description>Delete entity and blob (combined)</description></item>
    ///   <item><term>PUT    api/objects/{path}/content?iri=…</term><description>Upload binary content (saga)</description></item>
    ///   <item><term>GET    api/objects/{path}/content?iri=…</term><description>Download binary content</description></item>
    ///   <item><term>DELETE api/objects/{path}/content?iri=…</term><description>Delete blob only; clears entity ObjectKey/ContentType</description></item>
    /// </list>
    /// <c>MapOperations()</c> skips <c>[ObjectBearing]</c> types; all routes for these entities
    /// are owned here. See <c>ObjectStorage.Http</c> ADR-0001 and root ADR-0019.
    /// </summary>
    public static IEndpointRouteBuilder MapObjectOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IExecutionAspectIriProvider opProvider =
            new HeaderExecutionAspectIriProvider(
                OperationEndpointsHttpServiceCollectionExtensions.AspectIriHeader);

        var descriptors = app.ServiceProvider.GetServices<ObjectOperationDescriptor>();

        var options = app.ServiceProvider
            .GetService<IOptions<ObjectStorageHttpOptions>>()?.Value
            ?? new ObjectStorageHttpOptions();

        foreach (var desc in descriptors)
        {
            RegisterObjectEndpointsForMethod
                .MakeGenericMethod(desc.EntityType)
                .Invoke(null, [app, desc, opProvider, options]);
        }

        return app;
    }

    private static void RegisterObjectEndpointsFor<T>(
        IEndpointRouteBuilder app,
        ObjectOperationDescriptor desc,
        IExecutionAspectIriProvider opProvider,
        ObjectStorageHttpOptions options)
        where T : class, IEntity
    {
        var entityPath = $"api/entities/{desc.Path}";
        var contentPath = $"api/objects/{desc.Path}/content";

        var logger = app.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Forge.ObjectStorage.Http.{typeof(T).Name}");

        // Reflect generated properties once at registration time — they are stable
        // per type and captured by all handler closures below.
        var objectKeyProp = typeof(T).GetProperty("ObjectKey")
            ?? throw new InvalidOperationException(
                $"[ObjectBearing] entity '{typeof(T).Name}' is missing the generated ObjectKey property. " +
                "Ensure [ObjectBearing] is applied and Forge.Entity.Generators is referenced as an Analyzer.");

        var contentTypeProp = typeof(T).GetProperty("ContentType")
            ?? throw new InvalidOperationException(
                $"[ObjectBearing] entity '{typeof(T).Name}' is missing the generated ContentType property.");

        // ── POST api/entities/{path} — Create ────────────────────────────────
        app.MapPost(entityPath, async (
            HttpContext ctx,
            JsonObject body,
            ITransactionalEntityStore store) =>
        {
            using var _ops = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                var entity = OperationEntityBinder.CreateFromJson<T>(body);

                var relError = await OperationEntityBinder.ValidateOwningRelationsAsync(entity, ctx.RequestAborted);
                if (relError is not null)
                    return Results.UnprocessableEntity(relError);

                await using var tx = EntityOperations.BeginTransaction();
                tx.Create(entity, aspectIri);
                await tx.CommitAsync();

                return Results.Ok(new OperationCreatedResponse(entity.Iri));
            });
        });

        // ── GET api/entities/{path}          — List  ─────────────────────────
        // ── GET api/entities/{path}?iri=…    — Read  ─────────────────────────
        app.MapGet(entityPath, async (
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

        // ── PUT api/entities/{path}?iri=… — Update metadata ──────────────────
        app.MapPut(entityPath, async (
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

                var relError = await OperationEntityBinder.ValidateOwningRelationsAsync(entity!, ctx.RequestAborted);
                if (relError is not null)
                    return Results.UnprocessableEntity(relError);

                await using var tx = EntityOperations.BeginTransaction();
                tx.Update(entity!, aspectIri);
                await tx.CommitAsync();

                return Results.Ok(new OperationUpdatedResponse(entity!.Iri));
            });
        });

        // ── DELETE api/entities/{path}?iri=… — Delete entity + blob ──────────
        // Combined: entity is removed from the RDF store and the associated blob is
        // deleted best-effort. No orphaned blobs are left behind.
        app.MapDelete(entityPath, async (
            HttpContext ctx,
            string iri,
            ITransactionalEntityStore store,
            IObjectStoreProvider objectStoreProvider) =>
        {
            using var _ops = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                // Load to capture the objectKey before deletion.
                var entity = await EntityOperations.ReadAsync<T>(iri);
                if (entity is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No entity with IRI '{iri}' was found."));

                var objectKey = objectKeyProp.GetValue(entity) as string;

                await using var tx = EntityOperations.BeginTransaction();
                tx.Delete<T>(iri, aspectIri);
                await tx.CommitAsync(ctx.RequestAborted);

                // Best-effort blob cleanup — logged but not fatal if entity was already gone.
                if (objectKey is not null)
                {
                    var objectStore = objectStoreProvider.GetStore(desc.StoreKey);
                    await TryDeleteBlobAsync(objectStore, objectKey, logger);
                }

                return Results.Ok(new OperationDeletedResponse());
            });
        });

        // ── PUT api/objects/{path}/content?iri=… — Upload binary (streaming) ──
        // The request body is streamed directly to the object store — no in-memory
        // buffering occurs. Sequence:
        //   1. Reject immediately when Content-Length header exceeds the configured limit.
        //   2. Stream request body to a new version-7 key.
        //   3. Update entity metadata (ObjectKey + ContentType) and commit via the
        //      aspect-enforcing transaction. If the aspect rejects, delete the just-written
        //      blob and return 422.
        //   4. Best-effort delete the previous blob (if the entity previously had one),
        //      so no orphaned objects are left behind on re-upload.
        app.MapPut(contentPath, async (
            HttpContext ctx,
            string iri,
            ITransactionalEntityStore store,
            IObjectStoreProvider objectStoreProvider) =>
        {
            using var _ops = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                return Results.BadRequest(new ExecutionError("INVALID_IRI",
                    $"The value '{iri}' is not a valid absolute IRI."));

            // Reject before reading the stream when Content-Length is declared and over limit.
            if (ctx.Request.ContentLength > options.MaxUploadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            // Accept multipart/form-data (file part named "content") or a raw binary body.
            Stream uploadStream;
            string requestContentType;
            if (ctx.Request.HasFormContentType)
            {
                var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
                var file = form.Files["content"];
                if (file is null)
                    return Results.BadRequest(new ExecutionError("MISSING_CONTENT",
                        "Expected a multipart form file part named 'content'." +
                        " Ensure the request body is multipart/form-data with a part named 'content'."));
                uploadStream = file.OpenReadStream();
                requestContentType = file.ContentType ?? "application/octet-stream";
            }
            else
            {
                uploadStream = ctx.Request.Body;
                requestContentType = ctx.Request.ContentType ?? "application/octet-stream";
            }

            var entity = await EntityOperations.ReadAsync<T>(iri);
            if (entity is null)
                return Results.NotFound(new ExecutionError("NOT_FOUND",
                    $"No entity with IRI '{iri}' was found."));

            var objectStore = objectStoreProvider.GetStore(desc.StoreKey);
            var newKey = $"{typeof(T).Name}/{Guid.CreateVersion7()}";
            var previousObjectKey = objectKeyProp.GetValue(entity) as string;

            // Step 1: stream to the object store.
            try
            {
                await objectStore.UploadAsync(newKey, uploadStream, requestContentType, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stream upload to key {Key}.", newKey);
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            // Step 2: update entity metadata and validate via the aspect engine.
            objectKeyProp.SetValue(entity, newKey);
            contentTypeProp.SetValue(entity, requestContentType);
            try
            {
                await using var tx = EntityOperations.BeginTransaction();
                tx.Update(entity, aspectIri);
                await tx.CommitAsync(ctx.RequestAborted);
            }
            catch (AspectException ex)
            {
                // Aspect rejected — blob was written but entity was not committed; clean up.
                await TryDeleteBlobAsync(objectStore, newKey, logger);
                return Results.UnprocessableEntity(new ExecutionError("ENTITY_SHACL_VIOLATION", ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update entity {Iri} after upload; deleting orphaned blob.", iri);
                await TryDeleteBlobAsync(objectStore, newKey, logger);
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            // Step 3: clean up the previous blob now the new one is committed (best-effort).
            if (previousObjectKey is not null)
                await TryDeleteBlobAsync(objectStore, previousObjectKey, logger);

            return Results.Ok(new OperationUpdatedResponse(iri));
        });

        // ── GET api/objects/{path}/content?iri=… — Download binary ───────────
        app.MapGet(contentPath, async (
            HttpContext ctx,
            string iri,
            IEntityStore store,
            IObjectStoreProvider objectStoreProvider) =>
        {
            using var _ops = EntityOperations.Use(store);

            if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                return Results.BadRequest(new ExecutionError("INVALID_IRI",
                    $"The value '{iri}' is not a valid absolute IRI."));

            var entity = await EntityOperations.ReadAsync<T>(iri);
            if (entity is null)
                return Results.NotFound(new ExecutionError("NOT_FOUND",
                    $"No entity with IRI '{iri}' was found."));

            // Explicit aspect gate (contextWhere only — no entity payload to run local SHACL against).
            var downloadAspectIri = ctx.Request.Headers["X-Forge-Operation-AspectIri"].FirstOrDefault();
            if (downloadAspectIri is { Length: > 0 }
                && ctx.RequestServices.GetService<IAspectStore>()?.TryResolveOperation(downloadAspectIri)
                    ?.ContextWhere is { } whereBody
                && store is ISparqlQueryStore queryStore)
            {
                var sparql =
                    $"SELECT ?focusNode ?message ?path WHERE {{ " +
                    $"VALUES ?entityIri {{ <{iri}> }} {whereBody} }}";

                await foreach (var row in queryStore.ExecuteSelectAsync(sparql, ctx.RequestAborted))
                {
                    var msg = row.GetLiteral("message") ?? "Download not permitted by aspect.";
                    return Results.UnprocessableEntity(new ExecutionError("ENTITY_ASPECT_VIOLATION", msg));
                }
            }

            var objectKey = objectKeyProp.GetValue(entity) as string;
            if (objectKey is null)
                return Results.NotFound(new ExecutionError("NO_CONTENT",
                    "Content has not yet been uploaded for this entity."));

            var objectStore = objectStoreProvider.GetStore(desc.StoreKey);
            var stream = await objectStore.DownloadAsync(objectKey, ctx.RequestAborted);
            var mimeType = contentTypeProp.GetValue(entity) as string ?? "application/octet-stream";

            return Results.Stream(stream, mimeType);
        });

        // ── DELETE api/objects/{path}/content?iri=… — Delete blob, clear entity fields ─
        app.MapDelete(contentPath, async (
            HttpContext ctx,
            string iri,
            ITransactionalEntityStore store,
            IObjectStoreProvider objectStoreProvider) =>
        {
            using var _ops = EntityOperations.Use(store);
            var aspectIri = await opProvider.GetAspectIriAsync(ctx) ?? Aspect.NoOpIri;

            return await ExecutionEndpointHelper.InvokeAsync(async () =>
            {
                if (!Uri.TryCreate(iri, UriKind.Absolute, out _))
                    return Results.BadRequest(new ExecutionError("INVALID_IRI",
                        $"The value '{iri}' is not a valid absolute IRI."));

                var entity = await EntityOperations.ReadAsync<T>(iri);
                if (entity is null)
                    return Results.NotFound(new ExecutionError("NOT_FOUND",
                        $"No entity with IRI '{iri}' was found."));

                var objectKey = objectKeyProp.GetValue(entity) as string;
                if (objectKey is null)
                    return Results.NotFound(new ExecutionError("NO_CONTENT",
                        "No blob content to delete for this entity."));

                var objectStore = objectStoreProvider.GetStore(desc.StoreKey);

                // Clear entity blob fields — entity stays in the RDF store.
                objectKeyProp.SetValue(entity, null);
                contentTypeProp.SetValue(entity, null);
                try
                {
                    await using var tx = EntityOperations.BeginTransaction();
                    tx.Update(entity, aspectIri);
                    await tx.CommitAsync(ctx.RequestAborted);
                }
                catch (AspectException ex)
                {
                    return Results.UnprocessableEntity(new ExecutionError("ENTITY_SHACL_VIOLATION", ex.Message));
                }

                // Best-effort blob delete — failure is logged but not fatal.
                await TryDeleteBlobAsync(objectStore, objectKey, logger);

                return Results.Ok(new OperationUpdatedResponse(iri));
            });
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task TryDeleteBlobAsync(IObjectStore store, string key, ILogger logger)
    {
        try
        {
            await store.DeleteAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to delete blob {Key}; the object may be orphaned and require manual cleanup.", key);
        }
    }
}
