using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Forge.Aspects.Abstractions;
using Forge.ObjectStorage;
using Forge.ObjectStorage.Http.DependencyInjection;
using Forge.ObjectStorage.InMemory.DependencyInjection;
using Forge.Operations.Http;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.ObjectStorage.Http.Tests;

/// <summary>
/// Integration tests for <see cref="ObjectOperationRouteBuilderExtensions.MapObjectOperations"/>.
/// A test host is built per test with an in-memory entity store and an in-memory object store,
/// verifying the eight-route REST contract for the <see cref="TestNote"/> entity.
/// </summary>
public sealed class MapObjectOperationsTests
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
                    var opts = Options.Create(new EntityRepositoryOptions());
                    var store = new InMemoryEntityStore(registry, opts);

                    services.AddRouting();
                    services.AddSingleton<IEntityStore>(store);
                    services.AddSingleton<ITransactionalEntityStore>(store);
                    services.AddForgeObjectStorageInMemory();
                    services.AddForgeObjectStorageHttpFromAssemblyContaining<TestNote>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(ep =>
                    {
                        ep.MapObjectOperations();
                        ep.MapOperations();
                    });
                });
            })
            .Build();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonSerializerOptions JsonOpts { get; } =
        new(JsonSerializerDefaults.Web);

    private static string ExtractIri(string json)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("iri").GetString()!;
    }

    private static MultipartFormDataContent CreateUploadForm(byte[] bytes, string mediaType)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(part, "content", "upload");
        return form;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_returns_ok_with_iri()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "Hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<OperationCreatedResponse>(body, JsonOpts);
        dto!.Iri.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task List_returns_empty_when_no_entities_exist()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("api/entities/test-notes");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Get_by_iri_returns_entity()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "ReadMe" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        var response = await client.GetAsync(
            $"api/entities/test-notes?iri={Uri.EscapeDataString(iri)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("title").GetString().ShouldBe("ReadMe");
    }

    [Fact]
    public async Task Download_returns_404_when_content_not_yet_uploaded()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "NoBlob" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        var response = await client.GetAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_content_returns_ok()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "Doc" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        var response = await client.PutAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}",
            CreateUploadForm(Encoding.UTF8.GetBytes("Hello Forge"), "text/plain"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = JsonSerializer.Deserialize<OperationUpdatedResponse>(
            await response.Content.ReadAsStringAsync(), JsonOpts);
        dto!.Iri.ShouldBe(iri);
    }

    [Fact]
    public async Task Upload_and_download_roundtrip()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "Round" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        // Upload
        var payload = Encoding.UTF8.GetBytes("Round-trip payload");
        var uploadResp = await client.PutAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}",
            CreateUploadForm(payload, "text/plain"));
        uploadResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Download
        var downloadResp = await client.GetAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}");
        downloadResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var bytes = await downloadResp.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes).ShouldBe("Round-trip payload");

        downloadResp.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
    }

    [Fact]
    public async Task Delete_content_clears_blob_and_entity_fields()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create then upload
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "ToDelete" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        await client.PutAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}",
            CreateUploadForm(Encoding.UTF8.GetBytes("bye"), "text/plain"));

        // Delete blob
        var deleteContent = await client.DeleteAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}");
        deleteContent.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Blob is gone (404 NO_CONTENT)
        var getContentResp = await client.GetAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}");
        getContentResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Entity still exists with objectKey cleared
        var getEntityResp = await client.GetAsync(
            $"api/entities/test-notes?iri={Uri.EscapeDataString(iri)}");
        getEntityResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await getEntityResp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("objectKey").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Delete_entity_also_removes_blob()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create then upload
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "ToDelete" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        await client.PutAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}",
            CreateUploadForm(Encoding.UTF8.GetBytes("bye"), "text/plain"));

        // Read entity to capture the internal objectKey before deletion.
        var entityJson = await (await client.GetAsync(
            $"api/entities/test-notes?iri={Uri.EscapeDataString(iri)}")).Content.ReadAsStringAsync();
        var objectKey = JsonDocument.Parse(entityJson).RootElement.GetProperty("objectKey").GetString();
        objectKey.ShouldNotBeNullOrWhiteSpace("blob should be set after upload");

        // Delete entity (combined: entity + blob)
        var deleteResp = await client.DeleteAsync(
            $"api/entities/test-notes?iri={Uri.EscapeDataString(iri)}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Entity is gone
        var getResp = await client.GetAsync(
            $"api/entities/test-notes?iri={Uri.EscapeDataString(iri)}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Blob is also gone — no orphan left behind.
        var objectStoreProvider = host.Services.GetRequiredService<IObjectStoreProvider>();
        var objectStore = objectStoreProvider.GetStore("test-notes-store");
        await Should.ThrowAsync<ObjectNotFoundException>(
            () => objectStore.DownloadAsync(objectKey!).AsTask());
    }

    [Fact]
    public async Task Download_blocked_by_contextWhere_gate_returns_422()
    {
        // Arrange: a gate aspect whose contextWhere always fires when title contains "blocked".
        const string gateAspectIri = "urn:test:download-gate";
        const string contextWhere = """
            ?entityIri <https://forge-it.net/predicates/test-notes/title> ?t .
            FILTER(CONTAINS(LCASE(STR(?t)), "blocked"))
            BIND(?entityIri AS ?focusNode)
            BIND("Download blocked by gate aspect." AS ?message)
            """;

        var gateAspect = new StubOperationAspect(gateAspectIri, contextWhere);
        var aspectStore = new StubAspectStore(gateAspect);

        using var host = BuildHostWithAspectStore(aspectStore);
        await host.StartAsync();
        var client = host.GetTestClient();

        // Create an entity whose title triggers the gate.
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "BLOCKED master" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        // Upload content so we reach the gate check (gate fires before objectKey check).
        await client.PutAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}",
            CreateUploadForm(Encoding.UTF8.GetBytes("secret"), "text/plain"));

        // Act: download with the gate aspect header.
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}");
        req.Headers.Add("X-Forge-Operation-AspectIri", gateAspectIri);
        var response = await client.SendAsync(req);

        // Assert: 422 Unprocessable Content with ENTITY_ASPECT_VIOLATION code.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().ShouldBe("ENTITY_ASPECT_VIOLATION");
    }

    [Fact]
    public async Task Download_proceeds_when_gate_contextWhere_does_not_fire()
    {
        const string gateAspectIri = "urn:test:download-gate";
        const string contextWhere = """
            ?entityIri <https://forge-it.net/predicates/test-notes/title> ?t .
            FILTER(CONTAINS(LCASE(STR(?t)), "blocked"))
            BIND(?entityIri AS ?focusNode)
            BIND("Download blocked by gate aspect." AS ?message)
            """;

        var gateAspect = new StubOperationAspect(gateAspectIri, contextWhere);
        var aspectStore = new StubAspectStore(gateAspect);

        using var host = BuildHostWithAspectStore(aspectStore);
        await host.StartAsync();
        var client = host.GetTestClient();

        // Title does NOT contain "blocked" → gate will not fire.
        var createResp = await client.PostAsJsonAsync(
            "api/entities/test-notes", new { title = "Approved master" });
        var iri = ExtractIri(await createResp.Content.ReadAsStringAsync());

        await client.PutAsync(
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}",
            CreateUploadForm(Encoding.UTF8.GetBytes("approved bytes"), "text/plain"));

        var req = new HttpRequestMessage(HttpMethod.Get,
            $"api/objects/test-notes/content?iri={Uri.EscapeDataString(iri)}");
        req.Headers.Add("X-Forge-Operation-AspectIri", gateAspectIri);
        var response = await client.SendAsync(req);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes).ShouldBe("approved bytes");
    }

    [Fact]
    public async Task MapOperations_skips_ObjectBearing_entity_to_avoid_double_registration()
    {
        // Arrange: manually register OperationEndpointDescriptor for TestNote
        // (simulating a caller that calls both AddOperationEndpointsHttp() and AddForgeObjectStorageHttp()).
        // MapOperations() must skip TestNote because it carries [ObjectBearing], so no duplicate
        // route conflict is raised and all routes continue to work via MapObjectOperations().
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var registry = new RdfMapperRegistry();
                    var opts = Options.Create(new EntityRepositoryOptions());
                    var store = new InMemoryEntityStore(registry, opts);

                    services.AddRouting();
                    services.AddSingleton<IEntityStore>(store);
                    services.AddSingleton<ITransactionalEntityStore>(store);
                    services.AddForgeObjectStorageInMemory();
                    services.AddForgeObjectStorageHttpFromAssemblyContaining<TestNote>();
                    // Simulate accidental double-registration of OperationEndpointDescriptor.
                    services.AddSingleton(new OperationEndpointDescriptor(typeof(TestNote), "test-notes"));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(ep =>
                    {
                        ep.MapObjectOperations(); // owns all eight TestNote routes
                        ep.MapOperations();       // must skip TestNote — would throw on duplicate if not skipped
                    });
                });
            })
            .Build();

        // Act: host starts without an InvalidOperationException (no duplicate route crash).
        await Should.NotThrowAsync(() => host.StartAsync());

        // Routes via MapObjectOperations() still function.
        var client = host.GetTestClient();
        var resp = await client.PostAsJsonAsync("api/entities/test-notes", new { title = "SkipTest" });
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Dispose();
    }

    // ── Host factory with IAspectStore ────────────────────────────────────────

    private static IHost BuildHostWithAspectStore(IAspectStore aspectStore)
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var registry = new RdfMapperRegistry();
                    var opts = Options.Create(new EntityRepositoryOptions());
                    var store = new InMemoryEntityStore(registry, opts);

                    services.AddRouting();
                    services.AddSingleton<IEntityStore>(store);
                    services.AddSingleton<ITransactionalEntityStore>(store);
                    services.AddSingleton<IAspectStore>(aspectStore);
                    services.AddForgeObjectStorageInMemory();
                    services.AddForgeObjectStorageHttpFromAssemblyContaining<TestNote>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(ep =>
                    {
                        ep.MapObjectOperations();
                        ep.MapOperations();
                    });
                });
            })
            .Build();
    }
}

// ── Test stubs ─────────────────────────────────────────────────────────────────

file sealed class StubOperationAspect(string iri, string contextWhere) : IOperationAspect
{
    public string Iri { get; } = iri;
    public string? LocalShapeTtl => null;
    public string? ContextWhere { get; } = contextWhere;
}

file sealed class StubAspectStore(IOperationAspect gate) : IAspectStore
{
    public void RegisterOperation(IOperationAspect aspect) { }
    public void RegisterQuery(IQueryAspect aspect) { }
    public void RegisterMessage(IMessageAspect aspect) { }
    public void RegisterCapabilityAspect(CapabilityAspect aspect) { }

    public IOperationAspect ResolveOperation(string iri) =>
        TryResolveOperation(iri) ?? throw new AspectNotFoundException(iri);
    public IQueryAspect ResolveQuery(string iri) => throw new NotSupportedException();
    public IMessageAspect ResolveMessage(string iri) => throw new NotSupportedException();
    public CapabilityAspect ResolveCapabilityAspect(string iri) => throw new NotSupportedException();

    public IOperationAspect? TryResolveOperation(string iri) =>
        iri == gate.Iri ? gate : null;
    public IQueryAspect? TryResolveQuery(string iri) => null;
    public IMessageAspect? TryResolveMessage(string iri) => null;
    public CapabilityAspect? TryResolveCapabilityAspect(string iri) => null;

    public IReadOnlyCollection<string> OperationIris => [];
    public IReadOnlyCollection<string> QueryIris => [];
    public IReadOnlyCollection<string> MessageIris => [];
    public IReadOnlyCollection<string> CapabilityAspectIris => [];
}
