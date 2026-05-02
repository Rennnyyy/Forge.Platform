using Forge.Entity.Aspects;
using Forge.Entity.Aspects.DependencyInjection;
using Forge.Entity.Repository;
using Forge.Entity.Repository.DependencyInjection;
using Forge.Entity.Repository.InMemory.DependencyInjection;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Entity.Aspects.Tests;

/// <summary>
/// Tests 1–4: engine pipeline, queue-order semantics, unregistered-aspect guard.
/// </summary>
[Collection("EntityOptions")]
public sealed class AspectEngineTests : IClassFixture<EntityOptionsFixture>
{
    // ------------------------------------------------------------------ Helpers

    private static ServiceProvider BuildProvider(Action<IShapeRegistry>? configureRegistry = null)
    {
        var services = new ServiceCollection();
        services.Configure<EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        services.AddForgeAspects();

        var sp = services.BuildServiceProvider();

        if (configureRegistry is not null)
            configureRegistry(sp.GetRequiredService<IShapeRegistry>());

        return sp;
    }

    private static Artist MakeArtist(string name = "Test Artist", string country = "us")
        => new() { Name = name, Country = country };

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalShape_violation_on_Create_rolls_back_and_entity_is_not_persisted()
    {
        const string requiredNamePattern = "REQUIRED_NAME";
        var localTtl = $@"
@prefix sh:   <http://www.w3.org/ns/shacl#> .
@prefix artist: <https://forge-it.net/artist/> .

<urn:shape:artist-name-required>
    a sh:NodeShape ;
    sh:targetClass <https://forge-it.net/types/artists> ;
    sh:property [
        sh:path artist:name ;
        sh:pattern ""{requiredNamePattern}"" ;
        sh:minCount 1 ;
        sh:message ""Artist name must match required pattern."" ;
    ] .
";
        var aspect = new InlineTtlShapeAspect("test-local-violation", localTtl, contextWhere: null);

        await using var sp = BuildProvider(r => r.Register(aspect, typeof(Artist), AspectKind.Create));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Wrong Name");
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist, aspect);

        var ex = await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        ex.SourceAspectName.ShouldBe("test-local-violation");
        ex.Violations.Count.ShouldBeGreaterThan(0);
        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContextShape_SPARQL_violation_on_Update_rolls_back()
    {
        const string contextWhere = @"
  BIND (?entityIri AS ?focusNode)
  BIND (""Context constraint always fails in test."" AS ?message)
";
        var aspect = new InlineTtlShapeAspect("test-context-violation", null, contextWhere);

        await using var sp = BuildProvider(r => r.Register(aspect, typeof(Artist), AspectKind.Update));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Existing Artist");
        await rawStore.SaveAsync(artist, WriteMode.Create);

        var updated = new Artist { Name = artist.Name, Country = "gb" };
        await using var tx = new EntityTransaction(txStore);
        tx.Update(updated, aspect);

        var ex = await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());
        ex.SourceAspectName.ShouldBe("test-context-violation");

        var reloaded = await rawStore.LoadAsync<Artist>(artist.Iri);
        reloaded.ShouldNotBeNull();
        reloaded!.Country.ShouldBe("us");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueueOrder_B_before_A_fails_A_before_B_succeeds()
    {
        var artistA = MakeArtist("Artist A", "us");
        var artistB = MakeArtist("Artist B", "gb");

        var contextWhere = $@"
  FILTER NOT EXISTS {{ <{artistA.Iri}> a ?t . }}
  BIND (<{artistB.Iri}> AS ?focusNode)
  BIND (""Artist A must exist before Artist B."" AS ?message)
";
        var aspectForB = new InlineTtlShapeAspect("requires-a", null, contextWhere);

        await using var sp = BuildProvider(r => r.Register(aspectForB, typeof(Artist), AspectKind.Create));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        // Scenario 1: B before A → fails
        await using (var tx = new EntityTransaction(txStore))
        {
            tx.Create(artistB, aspectForB).Create(artistA);
            await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());
        }

        (await rawStore.LoadAsync<Artist>(artistA.Iri)).ShouldBeNull();
        (await rawStore.LoadAsync<Artist>(artistB.Iri)).ShouldBeNull();

        // Scenario 2: A before B → succeeds
        await using (var tx = new EntityTransaction(txStore))
        {
            tx.Create(artistA).Create(artistB, aspectForB);
            await tx.CommitAsync();
        }

        (await rawStore.LoadAsync<Artist>(artistA.Iri)).ShouldNotBeNull();
        (await rawStore.LoadAsync<Artist>(artistB.Iri)).ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unregistered_aspect_throws_AspectNotRegisteredException_at_commit()
    {
        var unregistered = new InlineTtlShapeAspect("unregistered-aspect", null, null);

        await using var sp = BuildProvider();
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist();
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist, unregistered);

        var ex = await Should.ThrowAsync<AspectNotRegisteredException>(() => tx.CommitAsync().AsTask());
        ex.AspectName.ShouldBe("unregistered-aspect");
        ex.EntityType.ShouldBe(typeof(Artist));
        ex.Kind.ShouldBe(AspectKind.Create);
        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bonus: NoOp skips validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoOp_aspect_bypasses_validation_and_entity_is_persisted()
    {
        await using var sp = BuildProvider();
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist();
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist);
        await tx.CommitAsync();

        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6 – Delete aspect
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_aspect_violation_rolls_back_entity_survives()
    {
        // A context SPARQL that always fires a violation, simulating
        // "a referential-integrity guard prevents deletion".
        const string contextWhere = @"
  BIND (?entityIri AS ?focusNode)
  BIND (""Delete rejected by integrity guard."" AS ?message)
";
        var aspect = new InlineTtlShapeAspect("delete-guard", null, contextWhere);

        await using var sp = BuildProvider(r => r.Register(aspect, typeof(Artist), AspectKind.Delete));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Protected Artist");
        await rawStore.SaveAsync(artist, WriteMode.Create);

        await using var tx = new EntityTransaction(txStore);
        tx.Delete<Artist>(artist.Iri, aspect);

        var ex = await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());
        ex.SourceAspectName.ShouldBe("delete-guard");
        ex.RejectedOperation.ShouldBeOfType<DeleteOperation>();

        // Entity must still exist — the transaction was rolled back.
        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Delete_aspect_passes_entity_is_removed()
    {
        // A context SPARQL that returns no rows → no violations → deletion proceeds.
        const string contextWhere = @"
  FILTER (false)
";
        var aspect = new InlineTtlShapeAspect("delete-allow", null, contextWhere);

        await using var sp = BuildProvider(r => r.Register(aspect, typeof(Artist), AspectKind.Delete));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Removable Artist");
        await rawStore.SaveAsync(artist, WriteMode.Create);

        await using var tx = new EntityTransaction(txStore);
        tx.Delete<Artist>(artist.Iri, aspect);
        await tx.CommitAsync();

        // Entity must be gone — the deletion was committed.
        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldBeNull();
    }
}
