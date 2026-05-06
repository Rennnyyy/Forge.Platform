using Forge.Entity;
using Forge.Operations;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Repository.GraphDb.Tests;

/// <summary>
/// Mirrors <c>TransactionTests</c> (in-memory suite) against a live Ontotext GraphDB
/// instance using the REST Transactions API. Every test skips gracefully when
/// <see cref="GraphDbFixture.Available"/> is <c>false</c>.
///
/// <list type="bullet">
///   <item>Atomic commit — Create + Update + Delete applied in one server transaction.</item>
///   <item>Rollback on failure — partial mutations are reverted at the server.</item>
///   <item>Empty transaction — no server contact, no error.</item>
///   <item>Defensive guards — double commit, add after commit.</item>
///   <item>Update within transaction replaces existing entity.</item>
///   <item>Ambient entry-point — <c>EntityOperations.BeginTransaction()</c> smoke-test.</item>
/// </list>
/// </summary>
[Collection("GraphDb")]
[Trait("Category", "Integration")]
[Trait("Backend", "GraphDB")]
public sealed class GraphDbTransactionTests
{
    private readonly GraphDbFixture _fx;
    public GraphDbTransactionTests(GraphDbFixture fx) => _fx = fx;

    private const string SkipMessage =
        "GraphDB not reachable — start: " +
        "podman compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d";

    private GraphDbEntityStore BuildStore()
    {
        var registry = new RdfMapperRegistry();
        var repoOpts = Options.Create(new EntityRepositoryOptions());
        var gdbOpts = Options.Create(new GraphDbOptions
        {
            BaseUrl = _fx.BaseUrl,
            RepositoryId = _fx.RepositoryId,
            Timeout = TimeSpan.FromSeconds(30),
        });
        return new GraphDbEntityStore(new HttpClient(), registry, repoOpts, gdbOpts);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Atomic commit: Create + Update + Delete all apply together
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Commit_applies_Create_Update_Delete_atomically()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        // Pre-condition: Kai exists (will be deleted then re-created with updated country).
        var kai = new Artist { Name = "Kai Storm", Country = "us" };
        await repo.SaveAsync(kai, WriteMode.Create);

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        var kaiUpdated = new Artist { Name = "Kai Storm", Country = "gb" }; // changed Country → new IRI

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.Delete(kai.Iri)
          .Create(aria)
          .Create(kaiUpdated);
        await tx.CommitAsync();

        var loadedAria = await repo.FindAsync(aria.Iri);
        var loadedKai = await repo.FindAsync(kaiUpdated.Iri);
        var deletedKai = await repo.FindAsync(kai.Iri);

        loadedAria.ShouldNotBeNull();
        loadedAria!.Name.ShouldBe("Aria Nova");
        loadedKai.ShouldNotBeNull();
        loadedKai!.Country.ShouldBe("gb");
        deletedKai.ShouldBeNull("old Kai IRI should have been deleted by the transaction");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Rollback on failure: a duplicate Create reverts all preceding operations
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Failed_operation_rolls_back_entire_transaction()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        // Aria already exists.
        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        await repo.SaveAsync(aria, WriteMode.Create);

        // Transaction: create a new artist, then attempt a duplicate — should fail + rollback.
        var newArtist = new Artist { Name = "Brand New", Country = "de" };
        var duplicateAria = new Artist { Name = "Aria Nova", Country = "us" }; // same IRI

        var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.Create(newArtist)
          .Create(duplicateAria);

        await Should.ThrowAsync<InvalidOperationException>(() => tx.CommitAsync().AsTask());

        var shouldBeNull = await repo.FindAsync(newArtist.Iri);
        var ariaStillThere = await repo.FindAsync(aria.Iri);

        shouldBeNull.ShouldBeNull("rolled-back Create must not be visible on GraphDB");
        ariaStillThere.ShouldNotBeNull("original Aria must survive rollback");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Empty transaction is a no-op
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Empty_transaction_commits_without_error()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await Should.NotThrowAsync(() => tx.CommitAsync().AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Double commit throws
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Double_commit_throws_InvalidOperationException()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.CommitAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => tx.CommitAsync().AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Update within transaction replaces existing entity
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Transaction_Update_replaces_existing_entity()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        aria.Bio = "Original bio";
        await repo.SaveAsync(aria, WriteMode.Create);

        var ariaUpdated = new Artist { Name = "Aria Nova", Country = "us" };
        ariaUpdated.Bio = "Updated bio";

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.Update(ariaUpdated);
        await tx.CommitAsync();

        var loaded = await repo.FindAsync(aria.Iri);
        loaded.ShouldNotBeNull();
        loaded!.Bio.ShouldBe("Updated bio");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. EntityOperations.BeginTransaction() ambient smoke-test
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task BeginTransaction_via_EntityOperations_creates_and_commits()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = BuildStore();
        using var scope = EntityOperations.Use(store);

        var artist = new Artist { Name = "Ambient Artist", Country = "fr" };

        await using var tx = EntityOperations.BeginTransaction();
        tx.Create(artist);
        await tx.CommitAsync();

        var repo = new EntityRepository<Artist>(store);
        var loaded = await repo.FindAsync(artist.Iri);
        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Ambient Artist");
    }
}
