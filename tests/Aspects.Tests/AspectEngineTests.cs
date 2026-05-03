using Forge.Entity;
using Forge.Aspects;
using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Operation;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Transaction;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Tests 1–4: engine pipeline, queue-order semantics, unregistered-aspect guard.
/// </summary>
[Collection("EntityOptions")]
public sealed class AspectEngineTests : IClassFixture<EntityOptionsFixture>
{
    // ------------------------------------------------------------------ Helpers

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.Configure<EntityRepositoryOptions>(_ => { });
        services.AddForgeEntityRepository().UseInMemory();
        configure?.Invoke(services);
        services.AddForgeAspects();
        return services.BuildServiceProvider();
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
        const string aspectIri = "https://forge-it.net/aspects/test/test-local-violation";
        var aspect = new InlineTtlOperationAspect(aspectIri, localTtl, contextWhere: null);

        await using var sp = BuildProvider(s => s.AddOperationAspect(aspect));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Wrong Name");
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist, aspect.Iri);

        var ex = await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        ex.SourceAspectIri.ShouldBe(aspectIri);
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
        const string aspectIri = "https://forge-it.net/aspects/test/test-context-violation";
        var aspect = new InlineTtlOperationAspect(aspectIri, null, contextWhere);

        await using var sp = BuildProvider(s => s.AddOperationAspect(aspect));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Existing Artist");
        await rawStore.SaveAsync(artist, WriteMode.Create);

        var updated = new Artist { Name = artist.Name, Country = "gb" };
        await using var tx = new EntityTransaction(txStore);
        tx.Update(updated, aspect.Iri);

        var ex = await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());
        ex.SourceAspectIri.ShouldBe(aspectIri);

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
        const string aspectIri = "https://forge-it.net/aspects/test/requires-a";
        var aspectForB = new InlineTtlOperationAspect(aspectIri, null, contextWhere);

        await using var sp = BuildProvider(s => s.AddOperationAspect(aspectForB));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        // Scenario 1: B before A → fails
        await using (var tx = new EntityTransaction(txStore))
        {
            tx.Create(artistB, aspectForB.Iri).Create(artistA);
            await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());
        }

        (await rawStore.LoadAsync<Artist>(artistA.Iri)).ShouldBeNull();
        (await rawStore.LoadAsync<Artist>(artistB.Iri)).ShouldBeNull();

        // Scenario 2: A before B → succeeds
        await using (var tx = new EntityTransaction(txStore))
        {
            tx.Create(artistA).Create(artistB, aspectForB.Iri);
            await tx.CommitAsync();
        }

        (await rawStore.LoadAsync<Artist>(artistA.Iri)).ShouldNotBeNull();
        (await rawStore.LoadAsync<Artist>(artistB.Iri)).ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unregistered_aspect_throws_AspectNotFoundException_at_commit()
    {
        const string unregisteredIri = "https://forge-it.net/aspects/test/unregistered-aspect";

        await using var sp = BuildProvider();
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist();
        await using var tx = new EntityTransaction(txStore);
        tx.Create(artist, unregisteredIri);

        var ex = await Should.ThrowAsync<AspectNotFoundException>(() => tx.CommitAsync().AsTask());
        ex.AspectIri.ShouldBe(unregisteredIri);
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
        const string aspectIri = "https://forge-it.net/aspects/test/delete-guard";
        var aspect = new InlineTtlOperationAspect(aspectIri, null, contextWhere);

        await using var sp = BuildProvider(s => s.AddOperationAspect(aspect));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Protected Artist");
        await rawStore.SaveAsync(artist, WriteMode.Create);

        await using var tx = new EntityTransaction(txStore);
        tx.Delete<Artist>(artist.Iri, aspect.Iri);

        var ex = await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());
        ex.SourceAspectIri.ShouldBe(aspectIri);
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
        const string aspectIri = "https://forge-it.net/aspects/test/delete-allow";
        var aspect = new InlineTtlOperationAspect(aspectIri, null, contextWhere);

        await using var sp = BuildProvider(s => s.AddOperationAspect(aspect));
        var txStore  = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Removable Artist");
        await rawStore.SaveAsync(artist, WriteMode.Create);

        await using var tx = new EntityTransaction(txStore);
        tx.Delete<Artist>(artist.Iri, aspect.Iri);
        await tx.CommitAsync();

        // Entity must be gone — the deletion was committed.
        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldBeNull();
    }
}
