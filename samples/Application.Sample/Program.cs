using Forge.Application.Sample;
using Forge.Aspects;
using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Message;
using Forge.Capability.DependencyInjection;
using Forge.Capability.Http;
using Forge.Capability.Http.DependencyInjection;
using Forge.Operations;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.GraphDb.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Aspect infrastructure ──────────────────────────────────────────────────
// Permissive when no aspects are registered; keeps the pipeline running without
// any policy enforcement in this demo.
builder.Services.AddForgeAspects();

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
// The generated CRUD handlers for Book (CreateBookHandler … ListBookHandler) and
// the hand-written demo handlers (GreetHandler, CreateItemHandler, UpdateItemHandler,
// PatchItemHandler) are all discovered automatically by scanning this assembly.
// This call MUST come before AddCapabilityHttp() (see Capability ADR-0011).
builder.Services.AddCapabilityHandlersFromAssemblyContaining<Book>();

// ── 5. HTTP transport ─────────────────────────────────────────────────────────
// Scans the handler registrations above and builds the endpoint metadata used
// by MapCapabilities(). Must be called AFTER AddCapabilityHandlers…().
builder.Services.AddCapabilityHttp();

var app = builder.Build();

// ── 6. EntityOperations middleware ────────────────────────────────────────────
// The generated handlers delegate to active-record methods such as
// entity.CreateAsync() which resolve the ambient IEntityStore via AsyncLocal.
// This middleware binds the DI-resolved store to the current request context so
// every capability handler within that request can call entity operations.
app.Use(async (ctx, next) =>
{
    var store = ctx.RequestServices.GetRequiredService<IEntityStore>();
    using var _ = EntityOperations.Use(store);
    await next(ctx);
});

// ── 7. Capability endpoints ───────────────────────────────────────────────────
// Auto-registers all endpoints derived from [Capability] on each handler.
// Handlers with [CrudCapabilityHandler] (generated) are routed under api/entities/:
//
//   POST  api/entities/books/create,read,update,delete,list                  (generated Book handlers)
//   POST  api/entities/data-records/create,read,update,delete,list           (generated DataRecord handlers)
//
// Hand-written handlers are routed under api/capabilities/:
//
//   POST  api/capabilities/demo/greet                                        (GreetHandler)
//   POST  api/capabilities/demo/catalog/items/create                         (CreateItemHandler)
//   PUT   api/capabilities/demo/catalog/items/update                         (UpdateItemHandler)
//   PATCH api/capabilities/demo/catalog/items/patch                          (PatchItemHandler)
//   POST  api/capabilities/demo/fault                                        (TriggerFaultHandler — always 422)
//   POST  api/capabilities/demo/aspect                                       (AspectDemoHandler — see sample ADR-0003)
app.MapCapabilities();

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
    Iri              = "urn:forge:aspects:capability:demo-v1",
    CommandAspectIri = "urn:forge:aspects:demo-command-v1",
});

app.Run();

// Expose Program for WebApplicationFactory<Program> in integration tests (ADR-0013).
public partial class Program { }
