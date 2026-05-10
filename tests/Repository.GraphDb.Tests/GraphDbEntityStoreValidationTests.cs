using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Repository;
using Forge.Repository.GraphDb;
using Forge.Repository.Mapping;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Repository.GraphDb.Tests;

/// <summary>
/// Unit tests for IRI validation in <see cref="GraphDbEntityStore"/> (fix #2 from the
/// architectural review). These tests do not require a live GraphDB instance: the
/// <see cref="ArgumentException"/> is thrown before any HTTP call is made.
/// </summary>
public sealed class GraphDbEntityStoreValidationTests
{
    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static GraphDbEntityStore BuildStore()
    {
        var http = new HttpClient();
        var registry = new RdfMapperRegistry();
        var repoOpts = Options.Create(new EntityRepositoryOptions());
        var gdbOpts = Options.Create(new GraphDbOptions());
        return new GraphDbEntityStore(http, registry, repoOpts, gdbOpts);
    }

    // ─── LoadAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_throws_ArgumentException_for_relative_iri()
    {
        await using var store = BuildStore();
        await Should.ThrowAsync<ArgumentException>(
            () => store.LoadAsync<Artist>("not-absolute").AsTask());
    }

    [Fact]
    public async Task LoadAsync_throws_ArgumentException_for_iri_containing_opening_angle_bracket()
    {
        await using var store = BuildStore();
        // A crafted IRI with '<' would escape the SPARQL angle-bracket delimiter before this fix.
        await Should.ThrowAsync<ArgumentException>(
            () => store.LoadAsync<Artist>("https://forge-it.net/artists/<injected>").AsTask());
    }

    [Fact]
    public async Task LoadAsync_accepts_valid_absolute_iri_and_reaches_http_layer()
    {
        await using var store = BuildStore();
        // A valid IRI passes validation and reaches the HTTP layer (no live server → HttpRequestException).
        await Should.ThrowAsync<HttpRequestException>(
            () => store.LoadAsync<Artist>("https://forge-it.net/artists/valid-uuid").AsTask());
    }

    // ─── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_throws_ArgumentException_for_relative_iri()
    {
        await using var store = BuildStore();
        await Should.ThrowAsync<ArgumentException>(
            () => store.DeleteAsync("relative/path").AsTask());
    }

    [Fact]
    public async Task DeleteAsync_throws_ArgumentException_for_iri_containing_opening_angle_bracket()
    {
        await using var store = BuildStore();
        await Should.ThrowAsync<ArgumentException>(
            () => store.DeleteAsync("https://forge-it.net/artists/<inject>").AsTask());
    }

    [Fact]
    public async Task DeleteAsync_accepts_valid_absolute_iri_and_reaches_http_layer()
    {
        await using var store = BuildStore();
        await Should.ThrowAsync<HttpRequestException>(
            () => store.DeleteAsync("https://forge-it.net/artists/valid-uuid").AsTask());
    }
}
