using Forge.Aspects.Abstractions;
using Forge.Authorization;
using Forge.Authorization.Http.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Authorization.Http.Tests;

/// <summary>
/// Behavioral tests for <see cref="AllowAllGuardStartupFilter"/>.
/// Verifies that startup succeeds or fails based on the
/// <see cref="AuthorizationOptions.RequireExplicitGuard"/> setting and the registered
/// <see cref="IAspectGuard"/> implementation.
/// </summary>
public sealed class AllowAllGuardStartupFilterTests
{
    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Minimal IAspectGuard that unconditionally allows — used as a real (non-AllowAll) stub.</summary>
    private sealed class RealGuardStub : IAspectGuard
    {
        public ValueTask AuthorizeAsync(string agentToken, string aspectToken,
            CancellationToken cancellationToken = default) => default;
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        return builder;
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. RequireExplicitGuard = true + AllowAllAspectGuard → throws
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Startup_throws_when_RequireExplicitGuard_is_true_and_only_AllowAll_guard_is_registered()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton<IAspectGuard>(AllowAllAspectGuard.Instance);
        builder.Services.Configure<AuthorizationOptions>(o => o.RequireExplicitGuard = true);
        builder.Services.AddForgeAuthorizationHttp();

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync());
        ex.Message.ShouldContain("RequireExplicitGuard");
        ex.Message.ShouldContain("AllowAllAspectGuard");

        await app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. RequireExplicitGuard = true + no guard registered → throws
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Startup_throws_when_RequireExplicitGuard_is_true_and_no_guard_is_registered()
    {
        var builder = CreateBuilder();
        // No IAspectGuard registered at all.
        builder.Services.Configure<AuthorizationOptions>(o => o.RequireExplicitGuard = true);
        builder.Services.AddForgeAuthorizationHttp();

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync());
        ex.Message.ShouldContain("RequireExplicitGuard");

        await app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. RequireExplicitGuard = true + real guard → succeeds
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Startup_succeeds_when_RequireExplicitGuard_is_true_and_real_guard_is_registered()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton<IAspectGuard, RealGuardStub>();
        builder.Services.Configure<AuthorizationOptions>(o => o.RequireExplicitGuard = true);
        builder.Services.AddForgeAuthorizationHttp();

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        await Should.NotThrowAsync(() => app.StartAsync());

        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. RequireExplicitGuard = false + AllowAllAspectGuard → succeeds
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Startup_succeeds_when_RequireExplicitGuard_is_false_even_with_AllowAll_guard()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton<IAspectGuard>(AllowAllAspectGuard.Instance);
        builder.Services.Configure<AuthorizationOptions>(o => o.RequireExplicitGuard = false);
        builder.Services.AddForgeAuthorizationHttp();

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        await Should.NotThrowAsync(() => app.StartAsync());

        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. RequireExplicitGuard = false + no guard → succeeds
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Startup_succeeds_when_RequireExplicitGuard_is_false_and_no_guard_is_registered()
    {
        var builder = CreateBuilder();
        // Neither a guard nor the option override — default option value is true but we
        // explicitly set it to false here to model the Development environment.
        builder.Services.Configure<AuthorizationOptions>(o => o.RequireExplicitGuard = false);
        builder.Services.AddForgeAuthorizationHttp();

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        await Should.NotThrowAsync(() => app.StartAsync());

        await app.StopAsync();
        await app.DisposeAsync();
    }
}
