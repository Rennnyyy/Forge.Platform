using Forge.Application.Sample;
using Forge.Aspects.DependencyInjection;
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
app.MapCapabilities();

app.Run();

// Expose Program for WebApplicationFactory<Program> in integration tests (ADR-0013).
public partial class Program { }
