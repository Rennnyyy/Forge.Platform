using Forge.Aspects.DependencyInjection;
using Forge.Capability.DependencyInjection;
using Forge.Capability.Http;
using Forge.Capability.Http.DependencyInjection;
using Forge.Capability.Http.Sample;

var builder = WebApplication.CreateBuilder(args);

// 1. Aspect infrastructure (message validation engine; permissive when no aspects are registered).
builder.Services.AddForgeAspects();

// 2. Register all capability handlers from this assembly via auto-discovery.
//    Equivalent to: AddCapabilityHandler<GreetCommand, GreetResponse, GreetHandler>()
builder.Services.AddCapabilityHandlersFromAssemblyContaining<GreetHandler>();

// 3. Wire HTTP transport — scans existing handler registrations and registers
//    the default ICapabilityAspectIriProvider (reads X-Forge-Capability-AspectIri header).
//    Must be called after all AddCapabilityHandler<>() calls.
builder.Services.AddCapabilityHttp();

var app = builder.Build();

// 4. Map one POST endpoint per registered capability.
//    POST /demo/greet  ←→  [Capability("demo.greet")] on GreetHandler
app.MapCapabilities();

app.Run();

// Expose Program for WebApplicationFactory<Program> in tests.
public partial class Program { }
