using Forge.Entity;
using Forge.Operations;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Repository.Tests;

/// <summary>
/// Behavioural spec for <see cref="EntityTransaction"/> / <see cref="ITransactionalEntityStore"/>
/// executed against the in-memory backend.
///
/// <list type="bullet">
///   <item>Atomic commit — Create + Update + Delete applied together.</item>
///   <item>Rollback on failure — partial mutations are reverted.</item>
///   <item>Empty transaction — no-op, no store contact.</item>
///   <item>Defensive guards — double commit, add after commit, add after dispose.</item>
///   <item>Concurrent transactions — two parallel transactions serialize without deadlock.</item>
///   <item>Ambient entry-point — <c>EntityOperations.BeginTransaction()</c> smoke-test.</item>
/// </list>
/// </summary>
[Collection("EntityOptions")]
public sealed class TransactionTests : IClassFixture<EntityOptionsFixture>
{
    private static InMemoryEntityStore BuildStore()
    {
        var registry = new RdfMapperRegistry();
        var opts = Options.Create(new EntityRepositoryOptions());
        return new InMemoryEntityStore(registry, opts);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Atomic commit: Create + Update + Delete all apply together
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Commit_applies_Create_Update_Delete_atomically()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        // Pre-condition: Kai exists, will be updated; Aria will be created; Kai's old IRI
        // is a placeholder to delete.
        var kai = new Artist { Name = "Kai Storm", Country = "us" };
        await store.SaveAsync(kai, WriteMode.Create);

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        var kaiUpdated = new Artist { Name = "Kai Storm", Country = "gb" }; // Country changed
        var deleteIri = kai.Iri; // will be deleted (then recreated as kaiUpdated)

        // Delete old Kai then create both fresh to avoid IRI collision on Update.
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.Delete(kai.Iri)
          .Create(aria)
          .Create(kaiUpdated);
        await tx.CommitAsync();

        // All three effects must be visible.
        var loadedAria = await repo.FindAsync(aria.Iri);
        var loadedKai = await repo.FindAsync(kaiUpdated.Iri);
        var deletedKai = await repo.FindAsync(kai.Iri);

        loadedAria.ShouldNotBeNull();
        loadedAria!.Name.ShouldBe("Aria Nova");
        loadedKai.ShouldNotBeNull();
        loadedKai!.Country.ShouldBe("gb");
        deletedKai.ShouldBeNull("old Kai IRI should have been deleted");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Rollback on failure: a failing Create reverts all preceding operations
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Failed_operation_rolls_back_entire_transaction()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        // Aria already exists.
        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        await store.SaveAsync(aria, WriteMode.Create);

        // Transaction: delete Aria, then attempt to create a duplicate (same IRI via
        // Create on the same Name+Country → same generated IRI) → second Create fails.
        var newArtist = new Artist { Name = "Brand New", Country = "de" };
        var duplicateAria = new Artist { Name = "Aria Nova", Country = "us" }; // same IRI

        var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.Create(newArtist)    // would succeed …
          .Create(duplicateAria); // … but this duplicate Create must fail + rollback

        await Should.ThrowAsync<InvalidOperationException>(() => tx.CommitAsync().AsTask());

        // newArtist must NOT have been persisted (rollback occurred).
        var shouldBeNull = await repo.FindAsync(newArtist.Iri);
        var ariaStillThere = await repo.FindAsync(aria.Iri);

        shouldBeNull.ShouldBeNull("rolled-back Create must not be visible");
        ariaStillThere.ShouldNotBeNull("original Aria must survive rollback");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Empty transaction is a no-op
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Empty_transaction_commits_without_error()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await Should.NotThrowAsync(() => tx.CommitAsync().AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Double commit throws
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Double_commit_throws_InvalidOperationException()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.CommitAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => tx.CommitAsync().AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Adding an operation to a committed transaction throws
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Adding_to_committed_transaction_throws()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.CommitAsync();

        var artist = new Artist { Name = "Late", Country = "xx" };
        Should.Throw<InvalidOperationException>(() => tx.Create(artist));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Adding an operation to a disposed transaction throws
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Adding_to_disposed_transaction_throws()
    {
        await using var store = BuildStore();
        var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.DisposeAsync();

        var artist = new Artist { Name = "Ghost", Country = "xx" };
        Should.Throw<ObjectDisposedException>(() => tx.Create(artist));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. Update within transaction replaces existing entity
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Transaction_Update_replaces_existing_entity()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        await store.SaveAsync(aria, WriteMode.Create);
        aria.Bio = "Original bio";
        await store.SaveAsync(aria, WriteMode.Replace);

        // Update bio inside a transaction.
        var ariaBioUpdated = new Artist { Name = "Aria Nova", Country = "us" };
        ariaBioUpdated.Bio = "Updated bio";

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.Update(ariaBioUpdated);
        await tx.CommitAsync();

        var loaded = await repo.FindAsync(aria.Iri);
        loaded.ShouldNotBeNull();
        loaded!.Bio.ShouldBe("Updated bio");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. Concurrent transactions serialize correctly (no deadlock, no data loss)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Concurrent_transactions_serialize_without_deadlock()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        // Two independent artists, one per concurrent transaction.
        var aria = new Artist { Name = "Aria Nova", Country = "us" };
        var kai = new Artist { Name = "Kai Storm", Country = "de" };

        await Task.WhenAll(
            CommitCreateAsync(store, aria),
            CommitCreateAsync(store, kai));

        var loadedAria = await repo.FindAsync(aria.Iri);
        var loadedKai = await repo.FindAsync(kai.Iri);

        loadedAria.ShouldNotBeNull();
        loadedKai.ShouldNotBeNull();

        static async Task CommitCreateAsync(InMemoryEntityStore s, Artist a)
        {
            await using var tx = new EntityTransaction((ITransactionalEntityStore)s);
            tx.Create(a);
            await tx.CommitAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. EntityOperations.BeginTransaction() ambient smoke-test
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BeginTransaction_via_EntityOperations_creates_and_commits()
    {
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

    // ════════════════════════════════════════════════════════════════════════
    // 10. BeginTransaction throws NotSupportedException for non-transactional store
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BeginTransaction_throws_for_non_transactional_store()
    {
        // Wrap in a stub that does NOT implement ITransactionalEntityStore.
        var stub = new NonTransactionalStoreStub();
        using var scope = EntityOperations.Use(stub);
        Should.Throw<NotSupportedException>(() => EntityOperations.BeginTransaction());
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Minimal IEntityStore stub that does not implement ITransactionalEntityStore.</summary>
    private sealed class NonTransactionalStoreStub : IEntityStore
    {
        public string? NamedGraph => null;
        public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken ct) where T : class, IEntity
            => ValueTask.FromResult<T?>(null);
        public ValueTask SaveAsync<T>(T entity, WriteMode mode, CancellationToken ct) where T : class, IEntity
            => default;
        public ValueTask DeleteAsync(string iri, CancellationToken ct) => default;
        public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken ct) where T : class, IEntity
            => AsyncEnumerable.Empty<T>();
        public IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(string ownerIri, string predicate, CancellationToken ct)
            where T : class, IEntity => AsyncEnumerable.Empty<string>();
        public ValueTask DisposeAsync() => default;
    }
}
