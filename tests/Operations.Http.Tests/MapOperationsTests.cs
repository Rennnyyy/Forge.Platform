using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Forge.Execution.Http;
using Forge.Operations.Http.DependencyInjection;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Operations.Http.Tests;

/// <summary>
/// Integration tests for <see cref="OperationEndpointsEndpointRouteBuilderExtensions.MapOperations"/>.
/// A test host is built per test class with an in-memory entity store, verifying the
/// full five-verb REST contract for a <see cref="TestWidget"/> (Random identity) entity.
/// </summary>
public sealed class MapOperationsTests
{
    // ── Host factory ─────────────────────────────────────────────────────────

    private static IHost BuildHost()
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var registry = new RdfMapperRegistry();
                    var opts     = Options.Create(new EntityRepositoryOptions());
                    var store    = new InMemoryEntityStore(registry, opts);

                    services.AddRouting();
                    services.AddSingleton<IEntityStore>(store);
                    services.AddSingleton<ITransactionalEntityStore>(store);
                    services.AddOperationEndpointsHttpFromAssemblyContaining<TestWidget>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(ep => ep.MapOperations());
                });
            })
            .Build();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonSerializerOptions JsonOpts { get; } = new(JsonSerializerDefaults.Web);

    private static string? ExtractIri(string responseBody)
    {
        var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.TryGetProperty("iri", out var iriEl)
            ? iriEl.GetString()
            : null;
    }

    // ── POST — Create ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Create_Returns200WithIri()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var body = new { label = "Sprocket", value = 7 };
        var response = await client.PostAsJsonAsync("api/entities/test-widgets", body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var iri  = ExtractIri(json);
        iri.ShouldNotBeNullOrWhiteSpace();
    }

    // ── GET (list) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_List_ReturnsCreatedItems()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create two widgets
        await client.PostAsJsonAsync("api/entities/test-widgets", new { label = "A", value = 1 });
        await client.PostAsJsonAsync("api/entities/test-widgets", new { label = "B", value = 2 });

        var response = await client.GetAsync("api/entities/test-widgets");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items");
        items.GetArrayLength().ShouldBe(2);
    }

    // ── GET (read) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Read_ReturnsCorrectEntity()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create and capture IRI
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-widgets", new { label = "ReadMe", value = 99 });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync())!;

        var response = await client.GetAsync($"api/entities/test-widgets?iri={Uri.EscapeDataString(iri)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("label").GetString().ShouldBe("ReadMe");
        doc.RootElement.GetProperty("value").GetInt32().ShouldBe(99);
    }

    [Fact]
    public async Task Get_Read_NotFound_Returns404()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var fakeIri = $"https://forge-it.net/test-widgets/{Guid.NewGuid()}";
        var response = await client.GetAsync(
            $"api/entities/test-widgets?iri={Uri.EscapeDataString(fakeIri)}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().ShouldBe("NOT_FOUND");
    }

    // ── PUT — Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_Update_ModifiesEntity()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-widgets", new { label = "Original", value = 1 });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync())!;

        // Update
        var putResp = await client.PutAsJsonAsync(
            $"api/entities/test-widgets?iri={Uri.EscapeDataString(iri)}",
            new { label = "Updated", value = 2 });

        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify
        var getResp = await client.GetAsync(
            $"api/entities/test-widgets?iri={Uri.EscapeDataString(iri)}");
        var doc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("label").GetString().ShouldBe("Updated");
        doc.RootElement.GetProperty("value").GetInt32().ShouldBe(2);
    }

    // ── DELETE ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesEntity()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-widgets", new { label = "ToDelete", value = 5 });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync())!;

        // Delete
        var deleteResp = await client.DeleteAsync(
            $"api/entities/test-widgets?iri={Uri.EscapeDataString(iri)}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify gone
        var getResp = await client.GetAsync(
            $"api/entities/test-widgets?iri={Uri.EscapeDataString(iri)}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── DI registration guard ─────────────────────────────────────────────────

    [Fact]
    public void AddOperationEndpointsHttp_RegistersDescriptorForAnnotatedType()
    {
        var services = new ServiceCollection();
        services.AddOperationEndpointsHttpFromAssemblyContaining<TestWidget>();
        var sp = services.BuildServiceProvider();

        var descriptors = sp.GetServices<OperationEndpointDescriptor>().ToList();

        descriptors.ShouldContain(d => d.EntityType == typeof(TestWidget) && d.Path == "test-widgets");
        descriptors.ShouldContain(d => d.EntityType == typeof(TestTag) && d.Path == "test-tags");
    }

    // ── ADR-0002: X-Forge-Operation-AspectIri used directly (no DI slot) ─────

    [Fact]
    public void AddOperationEndpointsHttp_Does_Not_Register_IExecutionAspectIriProvider()
    {
        // ADR-0002: Operations.Http must not compete with Capability.Http for the
        // shared IExecutionAspectIriProvider DI slot.
        var services = new ServiceCollection();
        services.AddOperationEndpointsHttpFromAssemblyContaining<TestWidget>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetService<IExecutionAspectIriProvider>();

        provider.ShouldBeNull(
            "AddOperationEndpointsHttp() must not register IExecutionAspectIriProvider " +
            "— the provider is created directly in MapOperations() (see Operations.Http ADR-0002).");
    }

    [Fact]
    public async Task MapOperations_CrudWorks_WhenExternalProviderIsPreRegistered()
    {
        // ADR-0002: even if a caller (e.g. Capability.Http) pre-registers
        // IExecutionAspectIriProvider pointing at a different header, Operations.Http
        // endpoint CRUD must still function correctly.
        using var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var registry = new RdfMapperRegistry();
                    var opts     = Options.Create(new EntityRepositoryOptions());
                    var store    = new InMemoryEntityStore(registry, opts);

                    services.AddRouting();
                    services.AddSingleton<IEntityStore>(store);
                    services.AddSingleton<ITransactionalEntityStore>(store);

                    // Simulate Capability.Http registering IExecutionAspectIriProvider
                    // with a different header name BEFORE AddOperationEndpointsHttp().
                    services.AddSingleton<IExecutionAspectIriProvider>(
                        _ => new HeaderExecutionAspectIriProvider("X-Forge-Capability-AspectIri"));

                    // Now register Operations.Http — must not override or be overridden.
                    services.AddOperationEndpointsHttpFromAssemblyContaining<TestWidget>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(ep => ep.MapOperations());
                });
            })
            .Build();

        await host.StartAsync();
        var client = host.GetTestClient();

        // Basic create + read round-trip must succeed.
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-widgets", new { label = "CoexistenceCheck", value = 42 });
        createResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());
        iri.ShouldNotBeNullOrWhiteSpace();

        var readResp = await client.GetAsync(
            $"api/entities/test-widgets?iri={Uri.EscapeDataString(iri!)}");
        readResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Dispose();
    }
}
