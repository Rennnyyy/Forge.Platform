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
//    Must be called after all AddCapabilityHandler<>() calls.
builder.Services.AddCapabilityHttp();

var app = builder.Build();

// Auto-registers:
//   POST  /demo/greet
//   POST  /demo/catalog/items/create
//   PUT   /demo/catalog/items/update   ([CapabilityEndpoint("PUT")])
//   PATCH /demo/catalog/items/patch    ([CapabilityEndpoint("PATCH")])
app.MapCapabilities();

app.Run();

// Expose Program for WebApplicationFactory<Program> in tests.
public partial class Program { }
