using Forge.Branch;
using Forge.Entity;
using Forge.Branch.Http;
using Forge.Branch.Http.DependencyInjection;
using Forge.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using BranchEntity = Forge.Branch.Branch;

namespace Forge.Branch.Http.Tests;

/// <summary>
/// Behavioural spec for <see cref="BranchScopeMiddleware"/>.
/// See Execution.Http ADR-0001.
/// </summary>
public sealed class BranchScopeMiddlewareTests
{
    private const string DefaultBranchIri = "https://forge-it.net/branches/main";

    // ── Test host factory ────────────────────────────────────────────────────

    private static IHost BuildHost(
        IBranchIriProvider? provider = null,
        string defaultBranch = DefaultBranchIri,
        Action<HttpContext>? handler = null,
        IEnumerable<string>? knownBranchIris = null)
    {
        var effectiveProvider = provider ?? new HeaderBranchIriProvider();
        var knownSet = knownBranchIris?.ToHashSet() ?? [];
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<IBranchIriProvider>(effectiveProvider);
                    services.Configure<BranchOptions>(o => o.DefaultBranchIri = defaultBranch);
                    services.AddKeyedSingleton<IEntityStore>(
                        "forge.branch.management",
                        new StubManagementStore(knownSet));
                });
                web.Configure(app =>
                {
                    app.UseBranchScope();
                    app.Run(ctx =>
                    {
                        handler?.Invoke(ctx);
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Valid header → BranchScope.Current populated + echo header written
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Valid_header_sets_BranchScope_and_echoes_effective_header()
    {
        const string branchIri = "https://forge-it.net/branches/feature-X";
        string? capturedScope = null;

        using var host = BuildHost(
            handler: ctx => { capturedScope = BranchScope.Current; },
            knownBranchIris: [branchIri]);
        await host.StartAsync();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add(HeaderBranchIriProvider.BranchIriRequestHeader, branchIri);
        var response = await client.SendAsync(req);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        capturedScope.ShouldBe(branchIri);
        response.Headers.GetValues(BranchScopeMiddleware.EffectiveBranchIriResponseHeader)
            .ShouldContain(branchIri);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Absent header → default branch IRI used
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Absent_header_falls_back_to_configured_default_branch()
    {
        string? capturedScope = null;

        using var host = BuildHost(
            defaultBranch: DefaultBranchIri,
            handler: ctx => { capturedScope = BranchScope.Current; });
        await host.StartAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        capturedScope.ShouldBe(DefaultBranchIri);
        response.Headers.GetValues(BranchScopeMiddleware.EffectiveBranchIriResponseHeader)
            .ShouldContain(DefaultBranchIri);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Invalid header → 400 Bad Request, pipeline short-circuits
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Invalid_header_returns_400_and_does_not_invoke_next()
    {
        bool handlerInvoked = false;

        using var host = BuildHost(handler: _ => { handlerInvoked = true; });
        await host.StartAsync();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add(HeaderBranchIriProvider.BranchIriRequestHeader, "not-a-valid-iri");
        var response = await client.SendAsync(req);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        handlerInvoked.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_header_response_body_contains_invalid_value()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add(HeaderBranchIriProvider.BranchIriRequestHeader, "relative/path");
        var response = await client.SendAsync(req);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("relative/path");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. BranchScope is not leaked across requests
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BranchScope_is_not_set_outside_request()
    {
        // After the request completes the ambient scope must have been restored to null.
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add(HeaderBranchIriProvider.BranchIriRequestHeader,
            "https://forge-it.net/branches/x");
        await client.SendAsync(req);

        BranchScope.Current.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Unknown branch IRI → 404 + pipeline short-circuits
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Unknown_branch_IRI_header_returns_404()
    {
        bool handlerInvoked = false;

        // No branch IRIs are registered in the management store stub.
        using var host = BuildHost(handler: _ => { handlerInvoked = true; });
        await host.StartAsync();
        var client = host.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add(HeaderBranchIriProvider.BranchIriRequestHeader,
            "https://forge-it.net/branches/does-not-exist");
        var response = await client.SendAsync(req);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        handlerInvoked.ShouldBeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // AddBranchHttp DI extension wires HeaderBranchIriProvider + BranchOptions
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddBranchHttp_registers_IBranchIriProvider_as_HeaderBranchIriProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddBranchHttp(config);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IBranchIriProvider>().ShouldBeOfType<HeaderBranchIriProvider>();
    }

    [Fact]
    public void AddBranchHttp_registers_BranchOptions_with_defaults()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddBranchHttp(config);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<BranchOptions>>().Value;
        opts.DefaultBranchIri.ShouldBe("https://forge-it.net/branches/main");
        opts.ManagementGraphIri.ShouldBe("https://forge-it.net/management");
    }

    [Fact]
    public void AddBranchHttp_binds_BranchOptions_from_configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Forge:Branch:DefaultBranchIri"] = "https://forge-it.net/branches/custom",
                ["Forge:Branch:ManagementGraphIri"] = "https://forge-it.net/custom-management",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBranchHttp(config);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<BranchOptions>>().Value;
        opts.DefaultBranchIri.ShouldBe("https://forge-it.net/branches/custom");
        opts.ManagementGraphIri.ShouldBe("https://forge-it.net/custom-management");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Null-argument guards on DI extensions
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddBranchHttp_throws_for_null_services()
    {
        var config = new ConfigurationBuilder().Build();
        Should.Throw<ArgumentNullException>(() =>
            BranchHttpServiceCollectionExtensions.AddBranchHttp(null!, config));
    }

    [Fact]
    public void AddBranchHttp_throws_for_null_configuration()
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentNullException>(() => services.AddBranchHttp(null!));
    }

    [Fact]
    public void UseBranchScope_throws_for_null_app()
    {
        Should.Throw<ArgumentNullException>(() =>
            BranchHttpApplicationBuilderExtensions.UseBranchScope(null!));
    }
}

/// <summary>
/// Stub <see cref="IEntityStore"/> for <see cref="BranchScopeMiddlewareTests"/>.
/// Returns a placeholder <see cref="Branch"/> for any IRI that was registered as known;
/// null otherwise. All mutation/query operations are unsupported.
/// </summary>
file sealed class StubManagementStore(IReadOnlySet<string> knownIris) : IEntityStore
{
    public string? NamedGraph => null;

    public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken cancellationToken = default)
        where T : class, IEntity
    {
        if (knownIris.Contains(iri))
            return ValueTask.FromResult((T?)(object)new BranchEntity { Name = "stub" });
        return ValueTask.FromResult<T?>(null);
    }

    public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace,
        CancellationToken cancellationToken = default)
        where T : class, IEntity => throw new NotSupportedException();

    public ValueTask DeleteAsync(string iri, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken cancellationToken = default)
        where T : class, IEntity => throw new NotSupportedException();

    public IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken cancellationToken = default)
        where T : class, IEntity => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
