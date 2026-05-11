using Forge.Aspects.Abstractions;
using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Operation;
using Forge.Entity;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Repository.Transaction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Regression tests for the snapshot-based rollback mechanism in
/// <see cref="AspectEnforcingTransactionalStore"/>. See Aspects ADR-0012.
///
/// Each test puts one or more operations in a transaction where a LATER operation
/// fails validation AFTER an EARLIER operation has already been applied. The tests
/// assert that the already-applied operations are correctly reversed.
/// </summary>
[Collection("EntityOptions")]
public sealed class AspectEnforcingTransactionalStoreRollbackTests : IClassFixture<EntityOptionsFixture>
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

    private static Artist MakeArtist(string name, string country = "us") =>
        new() { Name = name, Country = country };

    /// <summary>
    /// Returns an aspect whose SHACL shape unconditionally rejects every artist
    /// (name must match a pattern that never matches).
    /// </summary>
    private static InlineTtlOperationAspect MakeRejectAllAspect(string aspectIri) =>
        new(aspectIri, RejectAllTtl(aspectIri), contextWhere: null);

    private static string RejectAllTtl(string aspectIri) => $@"
@prefix sh:     <http://www.w3.org/ns/shacl#> .
@prefix artist: <https://forge-it.net/artist/> .

<{aspectIri}-shape>
    a sh:NodeShape ;
    sh:targetClass <https://forge-it.net/types/artists> ;
    sh:property [
        sh:path artist:name ;
        sh:pattern ""IMPOSSIBLE_MATCH_\\d{{99}}"" ;
        sh:minCount 1 ;
        sh:message ""Test always-fail shape."" ;
    ] .
";

    // ─────────────────────────────────────────────────────────────────────────
    // T1 — Create (NoOp, applied) + Create (fails) → first Create rolled back
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_followed_by_failing_Create_rolls_back_first_create()
    {
        const string aspectIri = "https://forge-it.net/aspects/test/rollback-t1";
        var reject = MakeRejectAllAspect(aspectIri);

        await using var sp = BuildProvider(s => s.AddOperationAspect(reject));
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artistA = MakeArtist("First Artist");   // NoOp: passes, gets applied
        var artistB = MakeArtist("Second Artist");  // reject-all: triggers rollback of artistA

        await using var tx = new EntityTransaction(txStore);
        tx.Create(artistA).Create(artistB, aspectIri);

        await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        // artistA was applied then rolled back — must be absent
        (await rawStore.LoadAsync<Artist>(artistA.Iri)).ShouldBeNull();
        // artistB was never applied
        (await rawStore.LoadAsync<Artist>(artistB.Iri)).ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T2 — Update (NoOp, applied) + Create (fails) → Update rolled back (snapshot restored)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_followed_by_failing_Create_restores_pre_update_snapshot()
    {
        const string aspectIri = "https://forge-it.net/aspects/test/rollback-t2";
        var reject = MakeRejectAllAspect(aspectIri);

        await using var sp = BuildProvider(s => s.AddOperationAspect(reject));
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        // Persist the entity before the transaction (country = "us").
        var original = MakeArtist("Existing Artist", "us");
        await rawStore.SaveAsync(original, WriteMode.Create);

        // Modify the country to "gb" — this Update will pass and be applied.
        var modified = new Artist { Name = original.Name, Country = "gb" };
        var toCreate = MakeArtist("New Artist");  // reject-all: triggers rollback

        await using var tx = new EntityTransaction(txStore);
        tx.Update(modified)             // NoOp: snapshot captured, applied
          .Create(toCreate, aspectIri); // reject-all: rollback of Update

        await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        // Entity must be restored to the pre-transaction state.
        var reloaded = await rawStore.LoadAsync<Artist>(original.Iri);
        reloaded.ShouldNotBeNull();
        reloaded!.Country.ShouldBe("us");
        // The entity that was never applied must remain absent.
        (await rawStore.LoadAsync<Artist>(toCreate.Iri)).ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T3 — Create (NoOp, applied) + Update (fails) → Create rolled back
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_followed_by_failing_Update_rolls_back_create()
    {
        const string aspectIri = "https://forge-it.net/aspects/test/rollback-t3";
        var reject = MakeRejectAllAspect(aspectIri);

        await using var sp = BuildProvider(s => s.AddOperationAspect(reject));
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        // Persist the entity that will be targeted by the Update.
        var existing = MakeArtist("Existing", "us");
        await rawStore.SaveAsync(existing, WriteMode.Create);

        var created = MakeArtist("Brand New");  // NoOp: will be applied then rolled back
        var failingUpdate = new Artist { Name = existing.Name, Country = "de" };

        await using var tx = new EntityTransaction(txStore);
        tx.Create(created)                   // NoOp: applied
          .Update(failingUpdate, aspectIri); // reject-all: rollback of Create

        await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        // Created entity must be absent after rollback.
        (await rawStore.LoadAsync<Artist>(created.Iri)).ShouldBeNull();
        // Existing entity must be unchanged (Update failed before being applied).
        var reloaded = await rawStore.LoadAsync<Artist>(existing.Iri);
        reloaded.ShouldNotBeNull();
        reloaded!.Country.ShouldBe("us");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T4 — Three ops: two pass, third fails → both rolled back in reverse order
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Three_ops_two_applied_third_fails_both_rolled_back()
    {
        const string aspectIri = "https://forge-it.net/aspects/test/rollback-t4";
        var reject = MakeRejectAllAspect(aspectIri);

        await using var sp = BuildProvider(s => s.AddOperationAspect(reject));
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var artistA = MakeArtist("Artist A");  // NoOp: applied
        var artistB = MakeArtist("Artist B");  // NoOp: applied
        var artistC = MakeArtist("Artist C");  // reject-all: triggers rollback

        await using var tx = new EntityTransaction(txStore);
        tx.Create(artistA).Create(artistB).Create(artistC, aspectIri);

        await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        (await rawStore.LoadAsync<Artist>(artistA.Iri)).ShouldBeNull();
        (await rawStore.LoadAsync<Artist>(artistB.Iri)).ShouldBeNull();
        (await rawStore.LoadAsync<Artist>(artistC.Iri)).ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DropGraph_is_allowed_through_without_aspect_validation()
    {
        // DropGraphOperation carries Aspect.NoOpIri, so the engine fast-paths.
        await using var sp = BuildProvider();
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        // Seed via raw store so the aspect enforcer is not involved.
        var artist = MakeArtist("Drop Test");
        await rawStore.SaveAsync(artist, WriteMode.Create);
        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldNotBeNull();

        await using var tx = new EntityTransaction(txStore);
        await tx.DropGraph(artist.Iri).CommitAsync();

        (await rawStore.LoadAsync<Artist>(artist.Iri)).ShouldBeNull();
    }

    [Fact]
    public async Task DropGraph_followed_by_failing_Create_DropGraph_is_not_rolled_back()
    {
        // DropGraphOperation carries Aspect.NoOpIri so no snapshot can be captured
        // (entity type is unknown at the Aspects layer). If a later operation fails
        // aspect validation, the DropGraph is NOT rolled back — it is an unconditional
        // destructive operation. Callers who need atomicity must handle this at the
        // backend transaction level (e.g. GraphDB's server-side transaction).
        const string aspectIri = "https://forge-it.net/aspects/test/rollback-drop";
        var reject = MakeRejectAllAspect(aspectIri);

        await using var sp = BuildProvider(s => s.AddOperationAspect(reject));
        var txStore = sp.GetRequiredService<ITransactionalEntityStore>();
        var rawStore = sp.GetRequiredService<IEntityStore>();

        var existing = MakeArtist("Existing");
        await rawStore.SaveAsync(existing, WriteMode.Create);

        var incoming = MakeArtist("Incoming");

        await using var tx = new EntityTransaction(txStore);
        tx.DropGraph(existing.Iri).Create(incoming, aspectIri);

        await Should.ThrowAsync<AspectViolationException>(() => tx.CommitAsync().AsTask());

        // DropGraph was applied and is NOT rolled back (no snapshot possible).
        (await rawStore.LoadAsync<Artist>(existing.Iri)).ShouldBeNull();
        // The failing Create was not applied.
        (await rawStore.LoadAsync<Artist>(incoming.Iri)).ShouldBeNull();
    }
}
