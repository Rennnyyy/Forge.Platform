using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Repository.GraphDb.Tests;

/// <summary>
/// Mirrors <c>InMemoryRepositoryTests</c> against a live Ontotext GraphDB instance.
/// Every test is reported as <b>Skipped</b> (via <c>Xunit.Skip.If</c>) when
/// <see cref="GraphDbFixture.Available"/> is <c>false</c> (no container runtime
/// available or Podman/Docker daemon not running). Bring GraphDB up with:
/// <code>docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d</code>
/// </summary>
[Collection("GraphDb")]
[Trait("Category", "Integration")]
[Trait("Backend", "GraphDB")]
public sealed class GraphDbRepositoryTests
{
    private readonly GraphDbFixture _fx;
    public GraphDbRepositoryTests(GraphDbFixture fx) => _fx = fx;

    // ── Store + repo factory ─────────────────────────────────────────────────

    private sealed record Stores(
        GraphDbEntityStore Store,
        EntityRepository<Artist> Artists,
        EntityRepository<Label> Labels,
        EntityRepository<Album> Albums,
        EntityRepository<Track> Tracks);

    private Stores Build()
    {
        var registry = new RdfMapperRegistry();
        var repoOpts = Options.Create(new EntityRepositoryOptions());
        var gdbOpts = Options.Create(new GraphDbOptions
        {
            BaseUrl = _fx.BaseUrl,
            RepositoryId = _fx.RepositoryId,
            Timeout = TimeSpan.FromSeconds(15),
        });
        var store = new GraphDbEntityStore(new HttpClient(), registry, repoOpts, gdbOpts);
        return new Stores(
            store,
            new EntityRepository<Artist>(store),
            new EntityRepository<Label>(store),
            new EntityRepository<Album>(store),
            new EntityRepository<Track>(store));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. All supported CLR scalar types round-trip on Artist
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task All_scalar_types_roundtrip_on_Artist()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        var externalId = Guid.NewGuid();
        var bornOn = new DateOnly(1990, 6, 15);
        var registeredAt = new DateTimeOffset(2010, 3, 1, 12, 0, 0, TimeSpan.FromHours(1));
        var website = new Uri("https://arianowa.example");

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        aria.Bio = "Indie-electronic producer from New York.";
        aria.Active = true;
        aria.DebutYear = 2010;
        aria.StreamCount = 4_200_000_000L;
        aria.AvgBpm = 128.5f;
        aria.Popularity = 8.75;
        aria.TotalEarnings = 123_456.78m;
        aria.BornOn = bornOn;
        aria.RegisteredAt = registeredAt;
        aria.ExternalId = externalId;
        aria.Website = website;
        await s.Store.SaveAsync(aria);

        var loaded = await s.Artists.LoadAsync(aria.Iri);
        loaded.Name.ShouldBe("Aria Nova");
        loaded.Country.ShouldBe("us");
        loaded.Bio.ShouldBe("Indie-electronic producer from New York.");
        loaded.Active.ShouldBeTrue();
        loaded.DebutYear.ShouldBe(2010);
        loaded.StreamCount.ShouldBe(4_200_000_000L);
        loaded.AvgBpm.ShouldBe(128.5f);
        loaded.Popularity.ShouldBe(8.75);
        loaded.TotalEarnings.ShouldBe(123_456.78m);
        loaded.BornOn.ShouldBe(bornOn);
        loaded.RegisteredAt.ShouldBe(registeredAt);
        loaded.ExternalId.ShouldBe(externalId);
        loaded.Website.ShouldBe(website);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Nullable scalars absent from graph when null
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Nullable_scalars_are_absent_from_graph_when_null()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        var kai = new Artist { Name = "Kai Storm", Country = "uk" };
        kai.DebutYear = 2015;
        await s.Store.SaveAsync(kai);

        var loaded = await s.Artists.LoadAsync(kai.Iri);
        loaded.Bio.ShouldBeNull();
        loaded.Website.ShouldBeNull();
        loaded.DebutYear.ShouldBe(2015);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3 & 4. Find / Load boundary checks
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Find_returns_null_for_unknown_iri()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        (await s.Albums.FindAsync("https://forge-it.net/albums/ghost")).ShouldBeNull();
    }

    [SkippableFact]
    public async Task Load_throws_EntityNotFoundException_for_unknown_iri()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        await Should.ThrowAsync<EntityNotFoundException>(
            () => s.Labels.LoadAsync("https://forge-it.net/labels/ghost").AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. N:1 — Album → Label
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Album_N_to_1_ref_to_label_persists_and_can_be_resolved()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        var merge = new Label { Slug = "merge" };
        merge.Name = "Merge Records";
        merge.FoundedYear = 1992;
        await s.Store.SaveAsync(merge);

        var eclipse = new Album { Title = "Eclipse", ReleaseYear = 2023 };
        eclipse.ReleasedBy = EntityRef<Label>.ForIri(merge.Iri);
        await s.Store.SaveAsync(eclipse);

        var loaded = await s.Albums.LoadAsync(eclipse.Iri);
        EntityRef<Label>? refVal = loaded.ReleasedBy;
        Assert.NotNull(refVal);
        refVal!.Iri.ShouldBe(merge.Iri);

        using var session = EntitySession.Begin(s.Store);
        var resolvedLabel = await refVal!;
        resolvedLabel.ShouldNotBeNull();
        resolvedLabel!.Name.ShouldBe("Merge Records");
        resolvedLabel.FoundedYear.ShouldBe(1992);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. 1:N — Label → Albums
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Label_1_to_N_album_collection_persists_and_iterates()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        using var session = EntitySession.Begin(s.Store);

        var merge = new Label { Slug = "merge" };
        merge.Name = "Merge Records";
        var eclipse = new Album { Title = "Eclipse", ReleaseYear = 2023 };
        var voltage = new Album { Title = "Voltage", ReleaseYear = 2024 };
        await s.Store.SaveAsync(eclipse);
        await s.Store.SaveAsync(voltage);
        await merge.Albums.AddAsync(eclipse);
        await merge.Albums.AddAsync(voltage);
        await s.Store.SaveAsync(merge);

        var loaded = await s.Labels.LoadAsync(merge.Iri);
        var titles = new List<string>();
        await foreach (var a in loaded.Albums) titles.Add(a.Title);
        titles.ShouldBe(new[] { "Eclipse", "Voltage" });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. 1:N ordered — Album → Tracks (rdf:List)
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Album_1_to_N_tracks_are_ordered_rdf_list()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        using var session = EntitySession.Begin(s.Store);

        var t1 = new Track { Title = "Sunrise", Position = 1, DurationSeconds = 240 };
        var t2 = new Track { Title = "Midnight Run", Position = 2, DurationSeconds = 195 };
        var t3 = new Track { Title = "Fade Out", Position = 3, DurationSeconds = 310 };
        await s.Store.SaveAsync(t1);
        await s.Store.SaveAsync(t2);
        await s.Store.SaveAsync(t3);

        var eclipse = new Album { Title = "Eclipse" };
        await eclipse.Tracks.AddAsync(t1);
        await eclipse.Tracks.AddAsync(t2);
        await eclipse.Tracks.AddAsync(t3);
        await s.Store.SaveAsync(eclipse);

        var loaded = await s.Albums.LoadAsync(eclipse.Iri);
        var seen = new List<(string Title, int Dur)>();
        await foreach (var t in loaded.Tracks) seen.Add((t.Title, t.DurationSeconds));
        seen.ShouldBe(new[] { ("Sunrise", 240), ("Midnight Run", 195), ("Fade Out", 310) });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. M:N — same Artist IRI on multiple Albums
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task ManyToMany_same_artist_on_multiple_albums()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        using var session = EntitySession.Begin(s.Store);

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        var kai = new Artist { Name = "Kai Storm", Country = "uk" };
        await s.Store.SaveAsync(aria);
        await s.Store.SaveAsync(kai);

        var eclipse = new Album { Title = "Eclipse" };
        await eclipse.Artists.AddAsync(aria);
        await eclipse.Artists.AddAsync(kai);
        await s.Store.SaveAsync(eclipse);

        var voltage = new Album { Title = "Voltage" };
        await voltage.Artists.AddAsync(aria);
        await s.Store.SaveAsync(voltage);

        var loadedEclipse = await s.Albums.LoadAsync(eclipse.Iri);
        var eclipseIris = new List<string>();
        await foreach (var a in loadedEclipse.Artists) eclipseIris.Add(a.Iri);
        eclipseIris.ShouldContain(aria.Iri);
        eclipseIris.ShouldContain(kai.Iri);

        var loadedVoltage = await s.Albums.LoadAsync(voltage.Iri);
        var voltageIris = new List<string>();
        await foreach (var a in loadedVoltage.Artists) voltageIris.Add(a.Iri);
        voltageIris.ShouldContain(aria.Iri);
        voltageIris.ShouldNotContain(kai.Iri);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. N:1 — Track → Artist
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Track_N_to_1_ref_to_performing_artist_persists_and_resolves()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        await s.Store.SaveAsync(aria);

        var sunrise = new Track { Title = "Sunrise", Position = 1, DurationSeconds = 240 };
        sunrise.PerformedBy = EntityRef<Artist>.ForIri(aria.Iri);
        await s.Store.SaveAsync(sunrise);

        var loaded = await s.Tracks.LoadAsync(sunrise.Iri);
        EntityRef<Artist>? perfRef = loaded.PerformedBy;
        Assert.NotNull(perfRef);
        perfRef!.Iri.ShouldBe(aria.Iri);

        using var session = EntitySession.Begin(s.Store);
        var resolvedArtist = await perfRef!;
        resolvedArtist!.Name.ShouldBe("Aria Nova");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 10. WriteMode.Replace
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Replace_mode_swaps_track_list_cleanly()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        using var session = EntitySession.Begin(s.Store);

        var t1 = new Track { Title = "Sunrise", Position = 1, DurationSeconds = 240 };
        var t2 = new Track { Title = "Fade Out", Position = 2, DurationSeconds = 310 };
        var t3 = new Track { Title = "Encore", Position = 3, DurationSeconds = 180 };
        await s.Store.SaveAsync(t1);
        await s.Store.SaveAsync(t2);
        await s.Store.SaveAsync(t3);

        var eclipse = new Album { Title = "Eclipse" };
        await eclipse.Tracks.AddAsync(t1);
        await eclipse.Tracks.AddAsync(t2);
        await s.Store.SaveAsync(eclipse);

        await eclipse.Tracks.AddAsync(t3);
        await s.Store.SaveAsync(eclipse, WriteMode.Replace);

        var loaded = await s.Albums.LoadAsync(eclipse.Iri);
        var titles = new List<string>();
        await foreach (var t in loaded.Tracks) titles.Add(t.Title);
        titles.ShouldBe(new[] { "Sunrise", "Fade Out", "Encore" });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 11. WriteMode.Create — rejects duplicate IRI
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Create_mode_rejects_duplicate_iri()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        var merge = new Label { Slug = "merge" };
        merge.Name = "Merge Records";
        await s.Store.SaveAsync(merge, WriteMode.Create);

        var dupe = new Label { Slug = "merge" };
        dupe.Name = "Duplicate";
        await Should.ThrowAsync<InvalidOperationException>(
            () => s.Store.SaveAsync(dupe, WriteMode.Create).AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 12. DeleteAsync — stops at referenced subjects
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Delete_album_does_not_remove_referenced_artists_or_label()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();
        using var session = EntitySession.Begin(s.Store);

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        var merge = new Label { Slug = "merge" };
        merge.Name = "Merge Records";
        await s.Store.SaveAsync(aria);
        await s.Store.SaveAsync(merge);

        var eclipse = new Album { Title = "Eclipse" };
        eclipse.ReleasedBy = EntityRef<Label>.ForIri(merge.Iri);
        await eclipse.Artists.AddAsync(aria);
        await s.Store.SaveAsync(eclipse);

        await s.Store.DeleteAsync(eclipse.Iri);

        (await s.Albums.FindAsync(eclipse.Iri)).ShouldBeNull();
        (await s.Artists.FindAsync(aria.Iri)).ShouldNotBeNull();
        (await s.Labels.FindAsync(merge.Iri)).ShouldNotBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 13. QueryAllAsync
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task QueryAll_returns_every_album_in_the_catalogue()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        foreach (var title in new[] { "Eclipse", "Voltage", "Drift" })
        {
            var a = new Album { Title = title, ReleaseYear = 2023 };
            await s.Store.SaveAsync(a);
        }

        var found = new List<Album>();
        await foreach (var a in s.Albums.QueryAllAsync()) found.Add(a);
        found.Count.ShouldBe(3);
        found.Select(a => a.Title).OrderBy(x => x)
             .ShouldBe(new[] { "Drift", "Eclipse", "Voltage" });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 14. UuidV5 determinism
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task UuidV5_artist_iri_is_deterministic_and_survives_reload()
    {
        Skip.If(!_fx.Available, "GraphDB not reachable — start: docker compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d");
        await _fx.ClearAsync();
        var s = Build();

        var aria1 = new Artist { Name = "Aria Nova", Country = "us" };
        aria1.DebutYear = 2010;
        await s.Store.SaveAsync(aria1);

        var aria2 = new Artist { Name = "Aria Nova", Country = "us" };
        aria2.Iri.ShouldBe(aria1.Iri);

        var loaded = await s.Artists.LoadAsync(aria1.Iri);
        loaded.Name.ShouldBe("Aria Nova");
        loaded.Country.ShouldBe("us");
        loaded.DebutYear.ShouldBe(2010);
    }
}
