using Forge.Application.Sample;
using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Message;
using Forge.Aspects.Operation;
using Forge.Branch;
using Forge.Branch.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Forge.Branch.Http;
using Forge.Branch.Http.DependencyInjection;
using Forge.Execution;
using Forge.Capability.DependencyInjection;
using Forge.Capability.Http;
using Forge.Capability.Http.DependencyInjection;
using Forge.Capability.Messaging;
using Forge.Capability.Messaging.DependencyInjection;
using Forge.Execution.Http.DependencyInjection;
using Forge.Operations;
using Forge.Operations.Http;
using Forge.Operations.Http.DependencyInjection;
using Forge.Repository;
using Forge.Authorization.Http.DependencyInjection;
using Forge.Repository.DependencyInjection;
using Forge.Repository.GraphDb.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Entity.Messaging.DependencyInjection;
using Forge.Messaging.InMemory.DependencyInjection;
using Forge.ObjectStorage.Http;
using Forge.ObjectStorage.Http.DependencyInjection;
using Forge.ObjectStorage.InMemory.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ── 0. Messaging — in-memory broker + entity change events ───────────────────
// MUST be called before AddForgeAspects() / AddForgeAuthorizationHttp() so that
// AddForgeEntityEvents() wins the unkeyed ITransactionalEntityStore slot.
// AddForgeAuthorizationHttp() captures whatever unkeyed ITransactionalEntityStore
// exists at registration time and wraps it with a GuardedTransactionalStore.
// If messaging is registered first, the chain becomes:
//   Guard → EventEmittingTransactionalStore → AspectEnforcing → Backend
// If messaging is registered after Authorization, EventEmitting is never inserted.
// See sample ADR-0010 and root ADR-0021.
builder.Services.AddForgeMessagingInMemory();
builder.Services.AddForgeEntityEvents();
builder.Services.AddForgeEntityMessaging<Book>(opts =>
{
    opts.TypeIri = "https://forge-it.net/entities/Book";
});
builder.Services.AddSingleton<EntityEventLog>();
builder.Services.AddSingleton<EntityStateCache>();
builder.Services.AddHostedService<BookHistoryConsumerService>();
builder.Services.AddHostedService<BookStateConsumerService>();

// ── 1. Aspect infrastructure ──────────────────────────────────────────────────
// Permissive when no aspects are registered; keeps the pipeline running without
// any policy enforcement in this demo.
builder.Services.AddForgeAspects();

// ── 1b. Authorization guard check ────────────────────────────────────────────
// Registers the startup filter that enforces Forge:Authorization:RequireExplicitGuard.
// The default (appsettings.json) sets RequireExplicitGuard = true, so the app will
// fail to start unless a real IAspectGuard is configured.
// appsettings.Development.json sets it to false — this sample uses AllowAllAspectGuard
// (no custom guard), which is acceptable for local development.
// See Authorization ADR-0004 and review flaw #1.
builder.Services.AddForgeAuthorizationHttp(builder.Configuration);

// ── 2. Entity store ───────────────────────────────────────────────────────────
// Backend is controlled by Forge:EntityRepository:Backend in appsettings.json.
// Supported values:
//   "InMemory"  — dotNetRDF in-memory store (default; no infrastructure required)
//   "GraphDb"   — Ontotext GraphDB over HTTP (configure Forge:GraphDb:* accordingly)
//
// Switch via environment variable:  Forge__EntityRepository__Backend=GraphDb
// Switch via named environment:     ASPNETCORE_ENVIRONMENT=GraphDb
//   (loads appsettings.GraphDb.json which sets Backend + GraphDb connection details)
var repoBuilder = builder.Services.AddForgeEntityRepository(builder.Configuration);
if (string.Equals(
        builder.Configuration["Forge:EntityRepository:Backend"],
        "GraphDb",
        StringComparison.OrdinalIgnoreCase))
    repoBuilder.UseGraphDb();
else
    repoBuilder.UseInMemory();

// ── 3. In-memory catalog store (used by the hand-written demo handlers) ───────
builder.Services.AddSingleton<ItemStore>();

// ── 3b. Branch infrastructure ─────────────────────────────────────────────────
// Registers the management-graph store (keyed "forge.branch.management"), populates
// BranchDefault.BranchIri from Forge:Branch:DefaultBranchIri, and starts the hosted
// service that upserts the default branch entity on first boot.
// AddForgeBranchHttp() also wires AspectEnforcingTransactionalStore onto the
// management store so that X-Forge-Operation-AspectIri is enforced on branch/snapshot
// CRUD. See root ADR-0019 and Branch.Http ADR-0001.
builder.Services.AddForgeBranchHttp(builder.Configuration);

// Registers HeaderBranchIriProvider (reads X-Forge-BranchIri request header) and
// binds BranchOptions. UseBranchScope() below activates the middleware.
builder.Services.AddBranchHttp(builder.Configuration);

// ── 4. Capability handlers ────────────────────────────────────────────────────
// Discovers hand-written capability handlers (GreetHandler, CreateItemHandler,
// UpdateItemHandler, PatchItemHandler, TriggerFaultHandler, AspectDemoHandler) by
// scanning this assembly. Must be called before AddCapabilityHttp() (Capability ADR-0011).
builder.Services.AddCapabilityHandlersFromAssemblyContaining<Book>();

// ── 5. HTTP transport (capability) ───────────────────────────────────────────
// Scans the handler registrations above and builds the endpoint metadata used
// by MapCapabilities(). Must be called AFTER AddCapabilityHandlers…().
builder.Services.AddCapabilityHttp();

// ── 5b. Messaging transport (capability) ─────────────────────────────────────
// Scans the same handler registrations and, for each handler carrying [Capability],
// derives topic names (forge.capabilities.{identity}.commands / .replies) and wires
// IAsyncCapabilityDispatcher, the command-pump, and the reply-pump automatically.
// Mirrors the AddCapabilityHttp() auto-discovery pattern. See root ADR-0022.
builder.Services.AddForgeCapabilityMessaging();

// ── 6. HTTP transport (entity operations) ────────────────────────────────────
// Scans the assembly for [Entity]+[OperationEndpoints] types (Book, DataRecord,
// Artist) and registers one OperationEndpointDescriptor per entity.
// MapOperations() wires the five REST endpoints per entity. See Operations.Http ADR-0001.
builder.Services.AddOperationEndpointsHttpFromAssemblyContaining<Book>();

// ── 6c. Object storage (in-memory) + HTTP layer ───────────────────────────────
// Registers the in-memory IObjectStoreProvider (keyed store per StoreKey) and scans
// the assembly for [ObjectBearing] types (TrackMaster). MapObjectOperations() wires
// the eight-route REST contract per entity (metadata CRUD + binary upload/download).
// See root ADR-0023 and ObjectStorage.Http ADR-0001.
builder.Services.AddForgeObjectStorageInMemory();
builder.Services.AddForgeObjectStorageHttpFromAssemblyContaining<TrackMaster>();

var app = builder.Build();

// ── 6b. Branch scope middleware ───────────────────────────────────────────────
// Reads X-Forge-BranchIri request header and activates BranchScope.Current for
// all downstream handlers. Must be registered before MapCapabilities/MapOperations.
// Echoes the effective IRI in X-Forge-Effective-BranchIri response header.
app.UseBranchScope();

// ── 6c. Snapshot immutability guard (HTTP translation) ───────────────────────
// Translates SnapshotImmutabilityViolationException into 422 Unprocessable Entity
// so that any attempt to write into a frozen snapshot graph returns a structured
// error instead of an unhandled 500. Must be placed before endpoint registrations.
app.Use(async (ctx, next) =>
{
    try { await next(ctx); }
    catch (SnapshotImmutabilityViolationException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await ctx.Response.WriteAsJsonAsync(new ExecutionError("SNAPSHOT_IMMUTABLE", ex.Message));
    }
});

// ── 7. Capability endpoints ───────────────────────────────────────────────────
// Auto-registers all endpoints derived from [Capability] on each handler.
// All capability handlers are routed under api/capabilities/:
//
//   POST  api/capabilities/demo/greet                                        (GreetHandler)
//   POST  api/capabilities/demo/catalog/items/create                         (CreateItemHandler)
//   PUT   api/capabilities/demo/catalog/items/update                         (UpdateItemHandler)
//   PATCH api/capabilities/demo/catalog/items/patch                          (PatchItemHandler)
//   POST  api/capabilities/demo/fault                                        (TriggerFaultHandler — always 422)
//   POST  api/capabilities/demo/aspect                                       (AspectDemoHandler — see sample ADR-0003)
app.MapCapabilities();

// ── 8. Entity operation endpoints ────────────────────────────────────────────
// Registers five REST endpoints per [OperationEndpoints] entity (Book, DataRecord, Artist):
//
//   POST   api/entities/books                  — Create
//   GET    api/entities/books                  — List
//   GET    api/entities/books?iri=…            — Read
//   PUT    api/entities/books?iri=…            — Update
//   DELETE api/entities/books?iri=…            — Delete
//
// Same pattern for api/entities/data-records and api/entities/artists.
// The optional X-Forge-Operation-AspectIri header activates IOperationAspect validation.
app.MapOperations();

// ── 8a2. Object-bearing entity endpoints ─────────────────────────────────────
// MapObjectOperations() owns all eight routes for every [ObjectBearing] entity.
// MapOperations() skips [ObjectBearing] types to avoid double-registration.
//
// TrackMaster routes:
//
//   POST   api/entities/track-masters              — Create metadata entity
//   GET    api/entities/track-masters              — List metadata entities
//   GET    api/entities/track-masters?iri=…        — Read single metadata entity
//   PUT    api/entities/track-masters?iri=…        — Update metadata entity
//   DELETE api/entities/track-masters?iri=…        — Delete entity + blob (combined)
//   PUT    api/objects/track-masters/content?iri=… — Upload binary asset (upload saga)
//   GET    api/objects/track-masters/content?iri=… — Download binary asset
//   DELETE api/objects/track-masters/content?iri=… — Delete blob only; entity stays
//
// See ObjectStorage.Http ADR-0001 and root ADR-0019.
app.MapObjectOperations();

// ── 8b. Branch management endpoints ──────────────────────────────────────────
// Maps five REST endpoints for Branch CRUD under api/branches.
// Uses the keyed management store ("forge.branch.management") so all ops target
// the management named graph. Delete atomically drops the branch's named graph.
//
//   POST   api/branches               — Create branch
//   GET    api/branches               — List all branches
//   GET    api/branches?iri=…         — Read a specific branch
//   PUT    api/branches?iri=…         — Update branch (description)
//   DELETE api/branches?iri=…         — Delete branch + drop its named graph
//
// Branch-scoped data (e.g. books) is read/written via api/entities/books with
// the X-Forge-BranchIri header — no additional endpoints are needed.
app.MapBranches();

// ── 8b2. Snapshot management endpoints ───────────────────────────────────────
// Maps two REST endpoints for Snapshot CRUD under api/snapshots.
// Snapshot listing/lookup is served by MapBranches() via ?type=snapshot and ?semver=.
//
//   POST   api/snapshots       — Create and seed a new snapshot
//   DELETE api/snapshots/{name} — Drop snapshot entity + named graph
app.MapSnapshots();

// ── 8b3. Messaging diagnostics endpoints ──────────────────────────────────────
// Two endpoints backed by two consumer services:
//
//   BookHistoryConsumerService → forge.entities.book.history → EntityEventLog
//     Append-only audit log: every mutation (Create/Update/Delete) in arrival order.
//     GET /api/diagnostics/entity-events          — all events
//     GET /api/diagnostics/entity-events?iri=…    — events for a single entity IRI
//
//   BookStateConsumerService → forge.entities.book.state → EntityStateCache
//     Compacted view: one entry per IRI, always the latest event (mirrors Kafka
//     log compaction).  Exposed as:
//     GET /api/diagnostics/entity-events/latest?iri=…
//
// Intended for local development and the Bruno messaging demo chapter (sample ADR-0010).
// In production remove or guard these endpoints behind authentication.
app.MapGet("/api/diagnostics/entity-events", (EntityEventLog log, string? iri) =>
    iri is not null ? log.GetByIri(iri) : (IReadOnlyList<EntityEventLogEntry>)log.GetAll());

// Compacted latest-state lookup — queries EntityStateCache (mirrors Kafka log compaction).
// Returns 200 with the entry object, or 404 when no events exist for that IRI.
app.MapGet("/api/diagnostics/entity-events/latest", (EntityStateCache cache, string iri) =>
{
    var entry = cache.GetLatest(iri);
    return entry is not null
        ? Results.Ok(entry)
        : Results.NotFound(new { code = "EVENT_NOT_FOUND", message = $"No events captured for IRI '{iri}'." });
});

// ── 8b4. Async capability dispatch endpoints ────────────────────────────────────
// These endpoints sit ALONGSIDE the normal Capability.Http route for AsyncProcessHandler
// (POST /api/capabilities/demo/async-process) and demonstrate the broker-mediated path.
//
// Fire-and-forget:
//   POST /api/async-capability/fire           — publishes command, returns 202 immediately
//
// Request-reply (PublishAndWaitAsync):
//   POST /api/async-capability/dispatch       — publishes command, awaits reply via
//                                              PendingReplyRegistry + CapabilityReplyListener,
//                                              returns the full ExecutionResult<AsyncProcessResponse>
//
// Both share the same AsyncProcessHandler; only the transport path differs.
// See sample ADR-0011.
app.MapPost("/api/async-capability/fire",
    async (AsyncProcessCommand command,
           IAsyncCapabilityDispatcher<AsyncProcessCommand, AsyncProcessResponse> dispatcher,
           CancellationToken ct) =>
    {
        await dispatcher.PublishAsync(command, cancellationToken: ct);
        return Results.Accepted();
    });

app.MapPost("/api/async-capability/dispatch",
    async (AsyncProcessCommand command,
           IAsyncCapabilityDispatcher<AsyncProcessCommand, AsyncProcessResponse> dispatcher,
           CancellationToken ct) =>
    {
        var result = await dispatcher.PublishAndWaitAsync(command, cancellationToken: ct);
        return result switch
        {
            ExecutionResult<AsyncProcessResponse>.Ok ok => Results.Ok(ok.Response),
            ExecutionResult<AsyncProcessResponse>.Fail fail => Results.UnprocessableEntity(fail.Error),
            _ => Results.StatusCode(500),
        };
    });

// ── 8. Capability aspect registration ────────────────────────────────────────
// Registers a demo IMessageAspect and CapabilityAspect directly on IAspectStore
// before the first request arrives. See sample ADR-0003 for the rationale for
// direct post-build registration over the AddMessageAspect DI helper.
//
// Active when the caller supplies the header:
//   X-Forge-Capability-AspectIri: urn:forge:aspects:capability:demo-v1
//
// The SHACL shape enforces that AspectDemoCommand.Name has at least one character.
var demoCommandAspect = new InlineTtlMessageAspect(
    iri: "urn:forge:aspects:demo-command-v1",
    shapeTtl: """
        @prefix sh:    <http://www.w3.org/ns/shacl#> .
        @prefix forge: <https://forge-it.net/> .

        <urn:forge:aspects:demo-command-shape>
            a sh:NodeShape ;
            sh:targetClass <urn:Forge.Application.Sample.AspectDemoCommand> ;
            sh:property [
                sh:path forge:Name ;
                sh:minLength 1 ;
                sh:message "Name must not be empty." ;
            ] .
        """);

var aspectStore = app.Services.GetRequiredService<IAspectStore>();
aspectStore.RegisterMessage(demoCommandAspect);
aspectStore.RegisterCapabilityAspect(new CapabilityAspect
{
    Iri = "urn:forge:aspects:capability:demo-v1",
    CommandAspectIri = "urn:forge:aspects:demo-command-v1",
});

// Permissive greeting demo aspect: registered so that callers can supply the IRI
// (X-Forge-Capability-AspectIri: urn:forge:aspects:demo-v1) without triggering an
// AspectNotFoundException. All three slots (command/response/event) are null, so
// the dispatcher performs no SHACL validation — this is intentional for the
// 01-greeting chapter demo which focuses on header passthrough, not shape enforcement.
aspectStore.RegisterCapabilityAspect(new CapabilityAspect
{
    Iri = "urn:forge:aspects:demo-v1",
});

// ── 9. Book entity operation aspects ─────────────────────────────────────────
// Demonstrates IOperationAspect validation on Book entity endpoints via
// EntityTransaction and MapOperations(). See sample ADR-0004, Aspects ADR-0010.
//
// Two operation aspects are registered directly; callers supply their IRIs via
//   X-Forge-Operation-AspectIri: <iri>
//
//   book-write-v1         : Local SHACL — publishedYear must be >= 1800.
//                           Use for Create and Update.
//   book-delete-v1        : Context SPARQL — rejects delete when available = false
//                           (cannot delete a checked-out book).
var bookWriteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:book-write-v1",
    localShapeTtl: """
        @prefix sh:    <http://www.w3.org/ns/shacl#> .
        @prefix xsd:   <http://www.w3.org/2001/XMLSchema#> .
        @prefix books: <https://forge-it.net/predicates/books/> .

        <urn:forge:aspects:shape:book-write-shape>
            a sh:NodeShape ;
            sh:targetClass <https://forge-it.net/types/books> ;
            sh:property [
                sh:path books:publishedYear ;
                sh:minInclusive "1800"^^xsd:integer ;
                sh:message "Published year must be 1800 or later." ;
            ] .
        """,
    contextWhere: null);

var bookDeleteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:book-delete-v1",
    localShapeTtl: null,
    contextWhere: """
        ?entityIri <https://forge-it.net/predicates/books/available> false .
        BIND(?entityIri AS ?focusNode)
        BIND("Cannot delete a checked-out book (available = false)." AS ?message)
        """);

aspectStore.RegisterOperation(bookWriteAspect);
aspectStore.RegisterOperation(bookDeleteAspect);

// ── 10. Book strict-update aspect (SHACL + WHERE combined) ───────────────────
// Demonstrates combining both validation passes in a single IOperationAspect:
//   Local SHACL pass: publishedYear must be >= 1800
//   Context WHERE pass: cannot update a checked-out book (available = false)
// The caller supplies:
//   X-Forge-Operation-AspectIri: urn:forge:aspects:operation:book-update-strict-v1
var bookUpdateStrictAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:book-update-strict-v1",
    localShapeTtl: """
        @prefix sh:    <http://www.w3.org/ns/shacl#> .
        @prefix xsd:   <http://www.w3.org/2001/XMLSchema#> .
        @prefix books: <https://forge-it.net/predicates/books/> .

        <urn:forge:aspects:shape:book-update-strict-shape>
            a sh:NodeShape ;
            sh:targetClass <https://forge-it.net/types/books> ;
            sh:property [
                sh:path books:publishedYear ;
                sh:minInclusive "1800"^^xsd:integer ;
                sh:message "Published year must be 1800 or later." ;
            ] .
        """,
    contextWhere: """
        ?entityIri <https://forge-it.net/predicates/books/available> false .
        BIND(?entityIri AS ?focusNode)
        BIND("Cannot update a checked-out book (available = false)." AS ?message)
        """);

aspectStore.RegisterOperation(bookUpdateStrictAspect);

// ── 11. Branch and Snapshot operation aspects ─────────────────────────────────
// Demonstrates IOperationAspect validation on the Branch and Snapshot management
// endpoints wired by AddForgeBranchHttp() + root ADR-0019.
// See Bruno chapter 16 (16-branch-aspect-demo/).
//
// Active when the caller supplies the header:
//   X-Forge-Operation-AspectIri: <iri>
//
// Branch aspects:
//   branch-write-v1   : Local SHACL — description must be present and >= 5 characters.
//                       Use with POST /api/branches and PUT /api/branches?iri=.
//   branch-delete-v1  : Context SPARQL — rejects delete when description contains
//                       the word "protected".
//                       Use with DELETE /api/branches?iri=.
//
// Snapshot aspects:
//   snapshot-write-v1  : Local SHACL — semVerMajor must be >= 1 (no 0.x snapshots).
//                        Use with POST /api/snapshots.
//   snapshot-delete-v1 : Context SPARQL — rejects delete when description contains
//                        the word "archived".
//                        Use with DELETE /api/snapshots?iri=.
var branchWriteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:branch-write-v1",
    localShapeTtl: """
        @prefix sh:     <http://www.w3.org/ns/shacl#> .
        @prefix branch: <https://forge-it.net/predicates/branch/> .

        <urn:forge:aspects:shape:branch-write-shape>
            a sh:NodeShape ;
            sh:targetClass <https://forge-it.net/types/branches> ;
            sh:property [
                sh:path branch:description ;
                sh:minCount 1 ;
                sh:minLength 5 ;
                sh:message "Branch must have a description of at least 5 characters." ;
            ] .
        """,
    contextWhere: null);

var branchDeleteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:branch-delete-v1",
    localShapeTtl: null,
    contextWhere: """
        ?entityIri <https://forge-it.net/predicates/branch/description> ?desc .
        FILTER(CONTAINS(LCASE(str(?desc)), "protected"))
        BIND(?entityIri AS ?focusNode)
        BIND("Cannot delete a branch whose description contains 'protected'." AS ?message)
        """);

var snapshotWriteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:snapshot-write-v1",
    localShapeTtl: """
        @prefix sh:       <http://www.w3.org/ns/shacl#> .
        @prefix xsd:      <http://www.w3.org/2001/XMLSchema#> .
        @prefix snapshot: <https://forge-it.net/predicates/snapshot/> .

        <urn:forge:aspects:shape:snapshot-write-shape>
            a sh:NodeShape ;
            sh:targetClass <https://forge-it.net/types/branches/Snapshot> ;
            sh:property [
                sh:path snapshot:semVerMajor ;
                sh:minCount 1 ;
                sh:minInclusive "1"^^xsd:integer ;
                sh:message "Snapshot semVerMajor must be at least 1 (no 0.x snapshots allowed)." ;
            ] .
        """,
    contextWhere: null);

var snapshotDeleteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:snapshot-delete-v1",
    localShapeTtl: null,
    contextWhere: """
        ?entityIri <https://forge-it.net/predicates/branch/description> ?desc .
        FILTER(CONTAINS(LCASE(str(?desc)), "archived"))
        BIND(?entityIri AS ?focusNode)
        BIND("Cannot delete a snapshot marked as 'archived' in its description." AS ?message)
        """);

aspectStore.RegisterOperation(branchWriteAspect);
aspectStore.RegisterOperation(branchDeleteAspect);
aspectStore.RegisterOperation(snapshotWriteAspect);
aspectStore.RegisterOperation(snapshotDeleteAspect);

// ── 12. TrackMaster operation aspects ─────────────────────────────────────────
// Demonstrates IOperationAspect validation on the [ObjectBearing] TrackMaster entity
// wired by MapObjectOperations(). See ObjectStorage.Http ADR-0001.
//
//   track-master-write-v1   : Local SHACL — title must be non-empty (minLength 1).
//                             Use on POST Create and PUT content upload.
//   track-master-lock-v1    : Context WHERE — rejects re-upload when the entity
//                             already has a blob key in the store (objectKey is set).
//                             Enforces single-master immutability: delete + recreate
//                             to replace a master take.
var trackMasterWriteAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:track-master-write-v1",
    localShapeTtl: """
        @prefix sh:  <http://www.w3.org/ns/shacl#> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .
        @prefix tm:  <https://forge-it.net/predicates/trackMaster/> .

        <urn:forge:aspects:shape:track-master-write-shape>
            a sh:NodeShape ;
            sh:targetClass <https://forge-it.net/types/track-masters> ;
            sh:property [
                sh:path tm:title ;
                sh:minCount 1 ;
                sh:minLength 1 ;
                sh:message "Track master title must not be empty." ;
            ] .
        """,
    contextWhere: null);

var trackMasterLockAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:track-master-lock-v1",
    localShapeTtl: null,
    contextWhere: """
        ?entityIri <https://forge-it.net/predicates/trackMaster/objectKey> ?existingKey .
        BIND(?entityIri AS ?focusNode)
        BIND("Master audio is already locked. Delete and recreate to replace it." AS ?message)
        """);

aspectStore.RegisterOperation(trackMasterWriteAspect);
aspectStore.RegisterOperation(trackMasterLockAspect);

// track-master-download-gate-v1 : Context WHERE — blocks GET /content when the entity's
//                                  title contains the word "restricted" (case-insensitive).
//                                  Demonstrates explicit download gating: pass the aspect IRI
//                                  via X-Forge-Operation-AspectIri on GET /content to enforce it.
var trackMasterDownloadGateAspect = new InlineTtlOperationAspect(
    iri: "urn:forge:aspects:operation:track-master-download-gate-v1",
    localShapeTtl: null,
    contextWhere: """
        ?entityIri <https://forge-it.net/predicates/trackMaster/title> ?t .
        FILTER(CONTAINS(LCASE(STR(?t)), "restricted"))
        BIND(?entityIri AS ?focusNode)
        BIND("Track master is flagged as restricted and cannot be downloaded." AS ?message)
        """);

aspectStore.RegisterOperation(trackMasterDownloadGateAspect);

app.Run();

// Expose Program for WebApplicationFactory<Program> in integration tests (ADR-0013).
public partial class Program { }
