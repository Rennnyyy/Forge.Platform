using Forge.Application.Sample;
using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Message;
using Forge.Aspects.Operation;
using Forge.Capability.DependencyInjection;
using Forge.Capability.Http;
using Forge.Capability.Http.DependencyInjection;
using Forge.Operations;
using Forge.Operations.Http;
using Forge.Operations.Http.DependencyInjection;
using Forge.Repository;
using Forge.Authorization.Http.DependencyInjection;
using Forge.Repository.DependencyInjection;
using Forge.Repository.GraphDb.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

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

// ── 4. Capability handlers ────────────────────────────────────────────────────
// Discovers hand-written capability handlers (GreetHandler, CreateItemHandler,
// UpdateItemHandler, PatchItemHandler, TriggerFaultHandler, AspectDemoHandler) by
// scanning this assembly. Must be called before AddCapabilityHttp() (Capability ADR-0011).
builder.Services.AddCapabilityHandlersFromAssemblyContaining<Book>();

// ── 5. HTTP transport (capability) ───────────────────────────────────────────
// Scans the handler registrations above and builds the endpoint metadata used
// by MapCapabilities(). Must be called AFTER AddCapabilityHandlers…().
builder.Services.AddCapabilityHttp();

// ── 6. HTTP transport (entity operations) ────────────────────────────────────
// Scans the assembly for [Entity]+[OperationEndpoints] types (Book, DataRecord,
// Artist) and registers one OperationEndpointDescriptor per entity.
// MapOperations() wires the five REST endpoints per entity. See Operations.Http ADR-0001.
builder.Services.AddOperationEndpointsHttpFromAssemblyContaining<Book>();

var app = builder.Build();

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

app.Run();

// Expose Program for WebApplicationFactory<Program> in integration tests (ADR-0013).
public partial class Program { }
