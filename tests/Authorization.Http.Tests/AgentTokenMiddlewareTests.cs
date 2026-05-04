using Forge.Authorization;
using Forge.Authorization.Http.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Authorization.Http.Tests;

/// <summary>
/// Behavioral tests for <see cref="AgentTokenMiddleware"/> via a real in-process test server.
/// </summary>
public sealed class AgentTokenMiddlewareTests
{
    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a test WebApplication with the agent-token middleware and a terminal handler
    /// that writes <c>AuthorizationContext.CurrentAgentToken ?? "(null)"</c> to the response body.
    /// </summary>
    private static async Task<WebApplication> BuildAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseAgentTokenMiddleware();
        app.MapGet("/token", (HttpContext ctx) => AuthorizationContext.CurrentAgentToken ?? "(null)");

        await app.StartAsync();
        return app;
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. Valid bearer token is established in ambient context
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Valid_bearer_token_is_established_in_ambient_context()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "agent-abc");

        var body = await client.GetStringAsync("/token");

        body.ShouldBe("agent-abc");
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. No Authorization header → no ambient token
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_authorization_header_yields_null_ambient_token()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        var body = await client.GetStringAsync("/token");

        body.ShouldBe("(null)");
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. Non-Bearer scheme is ignored
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_bearer_scheme_yields_null_ambient_token()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        var body = await client.GetStringAsync("/token");

        body.ShouldBe("(null)");
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. Bearer with empty token value is ignored
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bearer_with_empty_token_yields_null_ambient_token()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        // Manually set header to "Bearer   " (whitespace only).
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer   ");

        var body = await client.GetStringAsync("/token");

        body.ShouldBe("(null)");
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. Token scope is restored after request completes (no leakage)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_does_not_leak_between_requests()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        // First request with a token.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "first-token");
        await client.GetStringAsync("/token");

        // Second request without a token — must not see the previous value.
        client.DefaultRequestHeaders.Remove("Authorization");
        var body = await client.GetStringAsync("/token");

        body.ShouldBe("(null)");
    }
}
