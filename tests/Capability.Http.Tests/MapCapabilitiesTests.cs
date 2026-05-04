using System.Net;
using System.Net.Http.Json;
using Forge.Aspects;
using Forge.Aspects.Message;
using Forge.Capability;
using Forge.Capability.DependencyInjection;
using Forge.Capability.Http;
using Forge.Capability.Http.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Forge.Capability.Http.Tests;

// ────────────────────────────────────────────────────────────────────────
// Test domain
// ────────────────────────────────────────────────────────────────────────

public sealed record PingCommand(string Input);
public sealed record PingResponse(string Output);

[Capability("test.ping")]
public sealed class PingHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Ok(new PingResponse("pong:" + command.Input)));
}

[Capability("test.fail")]
public sealed class FailingHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Fail(
                new CapabilityError("TEST_ERROR", "intentional failure")));
}

// Handler without [Capability] — used for error-case tests only.
public sealed class NoAttributeHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Ok(new PingResponse("ok")));
}

// Handlers with bodyless methods — used for ADR-0005 guard tests only.
[Capability("test.get")]
[CapabilityEndpoint("GET")]
public sealed class GetHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Ok(new PingResponse("ok")));
}

[Capability("test.delete")]
[CapabilityEndpoint("DELETE")]
public sealed class DeleteHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Ok(new PingResponse("ok")));
}

// Handler with [CrudCapabilityHandler] — used to verify api/entities/ routing (ADR-0006).
[Capability("test.crud-ping")]
[CrudCapabilityHandler]
public sealed class CrudPingHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Ok(new PingResponse("pong:" + command.Input)));
}

// ────────────────────────────────────────────────────────────────────────
// Helper to capture the aspect IRI seen by the dispatcher
// ────────────────────────────────────────────────────────────────────────

public sealed class CapturingHandler : ICapabilityHandler<PingCommand, PingResponse>
{
    public string? CapturedCapabilityAspectIri { get; private set; }

    public ValueTask<CapabilityResult<PingResponse>> HandleAsync(
        PingCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        CapturedCapabilityAspectIri = context.Aspect?.Iri;
        return ValueTask.FromResult<CapabilityResult<PingResponse>>(
            new CapabilityResult<PingResponse>.Ok(new PingResponse("ok")));
    }
}

/// <summary>
/// Integration tests for <see cref="EndpointRouteBuilderExtensions.MapCapabilities"/>.
/// Uses a real in-process <see cref="TestServer"/>.
/// </summary>
public sealed class MapCapabilitiesTests
{
    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a started WebApplication with permissive aspect mocks and the given
    /// handler registration, then invokes <see cref="EndpointRouteBuilderExtensions.MapCapabilities"/>.
    /// Caller is responsible for disposing the returned <see cref="WebApplication"/>.
    /// </summary>
    private static async Task<WebApplication> BuildAsync(
        Action<IServiceCollection> registerHandlers)
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var store  = Substitute.For<IAspectStore>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(store);
        registerHandlers(builder.Services);
        builder.Services.AddCapabilityHttp();

        var app = builder.Build();
        app.MapCapabilities();
        await app.StartAsync();

        return app;
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. POST to derived route returns 200 with Ok result
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ok_result_returns_200_with_response_body()
    {
        await using var app = await BuildAsync(services =>
            services.AddCapabilityHandler<PingCommand, PingResponse, PingHandler>());
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/capabilities/test/ping", new PingCommand("hello"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PingResponse>();
        body.ShouldNotBeNull();
        body!.Output.ShouldBe("pong:hello");
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. POST to derived route returns 422 with Fail result
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fail_result_returns_422_with_error_body()
    {
        await using var app = await BuildAsync(services =>
            services.AddCapabilityHandler<PingCommand, PingResponse, FailingHandler>());
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/capabilities/test/fail", new PingCommand("x"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<CapabilityError>();
        body.ShouldNotBeNull();
        body!.Code.ShouldBe("TEST_ERROR");
        body.Message.ShouldBe("intentional failure");
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. X-Forge-Capability-AspectIri header is forwarded to the dispatcher
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aspect_iri_header_is_forwarded_to_dispatcher()
    {
        const string aspectIri = "urn:forge:test-aspect";

        // Use a real store that returns a CapabilityAspect when queried by the IRI.
        var store = Substitute.For<IAspectStore>();
        var capAspect = new CapabilityAspect { Iri = aspectIri };
        store.TryResolveCapabilityAspect(aspectIri).Returns(capAspect);

        var engine = Substitute.For<IMessageAspectEngine>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(store);
        builder.Services.AddCapabilityHandler<PingCommand, PingResponse, PingHandler>();
        builder.Services.AddCapabilityHttp();

        await using var app = builder.Build();
        app.MapCapabilities();
        await app.StartAsync();
        var client = app.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/capabilities/test/ping")
        {
            Content = JsonContent.Create(new PingCommand("z")),
        };
        request.Headers.Add(HeaderCapabilityAspectIriProvider.HeaderName, aspectIri);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // The store must have been queried with the forwarded IRI.
        store.Received(1).TryResolveCapabilityAspect(aspectIri);
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. Missing [Capability] attribute throws at MapCapabilities() time
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_capability_attribute_throws_at_map_time()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var store  = Substitute.For<IAspectStore>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(store);
        builder.Services.AddCapabilityHandler<PingCommand, PingResponse, NoAttributeHandler>();
        builder.Services.AddCapabilityHttp();

        await using var app = builder.Build();

        var ex = Should.Throw<InvalidOperationException>(() => app.MapCapabilities());
        ex.Message.ShouldContain(nameof(NoAttributeHandler));
        ex.Message.ShouldContain("[Capability]");
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. Route path is correctly derived from capability identity
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Route_path_is_derived_from_identity()
    {
        await using var app = await BuildAsync(services =>
            services.AddCapabilityHandler<PingCommand, PingResponse, PingHandler>());
        var client = app.GetTestClient();

        // "test.ping" → "/api/capabilities/test/ping"
        var response = await client.PostAsJsonAsync("/api/capabilities/test/ping", new PingCommand("route-test"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────────────
    // 6. Absent aspect IRI header dispatches permissively (null IRI)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Absent_aspect_iri_header_dispatches_permissively()
    {
        var store  = Substitute.For<IAspectStore>();
        var engine = Substitute.For<IMessageAspectEngine>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(store);
        builder.Services.AddCapabilityHandler<PingCommand, PingResponse, PingHandler>();
        builder.Services.AddCapabilityHttp();

        await using var app = builder.Build();
        app.MapCapabilities();
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/capabilities/test/ping", new PingCommand("permissive"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // No aspect IRI → store was never asked to resolve a capability aspect.
        store.DidNotReceive().TryResolveCapabilityAspect(Arg.Any<string>());
    }

    // ────────────────────────────────────────────────────────────────────
    // 7. GET handler throws at MapCapabilities() time (ADR-0005)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_method_throws_at_map_time()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var store  = Substitute.For<IAspectStore>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(store);
        builder.Services.AddCapabilityHandler<PingCommand, PingResponse, GetHandler>();
        builder.Services.AddCapabilityHttp();

        await using var app = builder.Build();

        var ex = Should.Throw<InvalidOperationException>(() => app.MapCapabilities());
        ex.Message.ShouldContain(nameof(GetHandler));
        ex.Message.ShouldContain("GET");
    }

    // ────────────────────────────────────────────────────────────────────
    // 8. DELETE handler throws at MapCapabilities() time (ADR-0005)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_method_throws_at_map_time()
    {
        var engine = Substitute.For<IMessageAspectEngine>();
        var store  = Substitute.For<IAspectStore>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(store);
        builder.Services.AddCapabilityHandler<PingCommand, PingResponse, DeleteHandler>();
        builder.Services.AddCapabilityHttp();

        await using var app = builder.Build();

        var ex = Should.Throw<InvalidOperationException>(() => app.MapCapabilities());
        ex.Message.ShouldContain(nameof(DeleteHandler));
        ex.Message.ShouldContain("DELETE");
    }

    // ────────────────────────────────────────────────────────────────────
    // 9. [CrudCapabilityHandler] routes handler under api/entities/
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrudCapabilityHandler_attribute_routes_under_api_entities_prefix()
    {
        await using var app = await BuildAsync(services =>
            services.AddCapabilityHandler<PingCommand, PingResponse, CrudPingHandler>());
        var client = app.GetTestClient();

        // [CrudCapabilityHandler] → "test.crud-ping" maps to /api/entities/test/crud-ping
        var response = await client.PostAsJsonAsync("/api/entities/test/crud-ping", new PingCommand("crud"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PingResponse>();
        body.ShouldNotBeNull();
        body!.Output.ShouldBe("pong:crud");
    }

    // ────────────────────────────────────────────────────────────────────
    // 10. Regular handler is NOT reachable under api/entities/
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Regular_handler_is_not_reachable_under_api_entities_prefix()
    {
        await using var app = await BuildAsync(services =>
            services.AddCapabilityHandler<PingCommand, PingResponse, PingHandler>());
        var client = app.GetTestClient();

        // PingHandler has no [CrudCapabilityHandler] → must NOT be reachable at api/entities/
        var response = await client.PostAsJsonAsync("/api/entities/test/ping", new PingCommand("x"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
