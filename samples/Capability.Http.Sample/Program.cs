using Forge.Aspects.DependencyInjection;
using Forge.Capability;
using Forge.Capability.DependencyInjection;
using Forge.Capability.Http;
using Forge.Capability.Http.DependencyInjection;
using Forge.Capability.Http.Sample;

var builder = WebApplication.CreateBuilder(args);

// 1. Aspect infrastructure (permissive when no aspects are registered).
builder.Services.AddForgeAspects();

// 2. In-memory store shared by all catalog handlers.
builder.Services.AddSingleton<ItemStore>();

// 3. Handlers auto-routed by MapCapabilities() (POST / PUT / PATCH).
//    These must be registered BEFORE AddCapabilityHttp() so they are included in
//    the snapshot used for auto-discovery.
builder.Services.AddCapabilityHandler<GreetCommand, GreetResponse, GreetHandler>();
builder.Services.AddCapabilityHandler<CreateItemCommand, CreateItemResponse, CreateItemHandler>();
builder.Services.AddCapabilityHandler<UpdateItemCommand, UpdateItemResponse, UpdateItemHandler>();
builder.Services.AddCapabilityHandler<PatchItemCommand, PatchItemResponse, PatchItemHandler>();

// 4. HTTP transport. Scans the registrations above and builds endpoint metadata.
//    Must be called after all auto-routed AddCapabilityHandler<>() calls.
builder.Services.AddCapabilityHttp();

// 5. GetItemHandler is registered AFTER AddCapabilityHttp() and is therefore NOT
//    auto-discovered by MapCapabilities(). The dispatcher is still in DI and is
//    injected into the manual MapGet endpoint below.
//    See Capability.Http ADR-0004 for the GET-with-route-param pattern.
builder.Services.AddCapabilityHandler<GetItemQuery, GetItemResponse, GetItemHandler>();

var app = builder.Build();

// Auto-registers:
//   POST  /demo/greet
//   POST  /demo/catalog/items/create
//   PUT   /demo/catalog/items/update   ([CapabilityEndpoint("PUT")])
//   PATCH /demo/catalog/items/patch    ([CapabilityEndpoint("PATCH")])
app.MapCapabilities();

// GET /demo/catalog/items/{id}
// Route-parameter binding requires explicit wiring; the full dispatcher pipeline
// (including aspect validation) is still exercised via ICapabilityDispatcher<,>.
app.MapGet("demo/catalog/items/{id}", async (
    Guid id,
    ICapabilityDispatcher<GetItemQuery, GetItemResponse> dispatcher,
    ICapabilityAspectIriProvider provider,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var iri    = await provider.GetCapabilityAspectIriAsync(httpContext, ct);
    var result = await dispatcher.DispatchAsync(new GetItemQuery(id), iri, ct);

    return result switch
    {
        CapabilityResult<GetItemResponse>.Ok ok     => Results.Ok(ok.Response),
        CapabilityResult<GetItemResponse>.Fail fail  => Results.UnprocessableEntity(fail.Error),
        _                                            => Results.StatusCode(500),
    };
});

app.Run();

// Expose Program for WebApplicationFactory<Program> in tests.
public partial class Program { }
