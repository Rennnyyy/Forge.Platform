using Forge.Entity;
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
/// Behavioural spec for <see cref="DropGraphOperation"/> and
/// <see cref="EntityTransaction.DropGraph"/>. See Repository ADR-0003.
///
/// <list type="bullet">
///   <item>Structure — <c>EntityIri</c> equals the graph IRI; <c>AspectIri</c> defaults to NoOp.</item>
///   <item>Guard — constructor rejects null/empty/whitespace graph IRI.</item>
///   <item>Builder — <c>EntityTransaction.DropGraph</c> returns the same instance (fluent).</item>
///   <item>Builder guard — <c>DropGraph</c> rejects null/empty/whitespace.</item>
///   <item>Builder state guards — <c>DropGraph</c> throws after commit and after dispose.</item>
/// </list>
/// </summary>
/// <remarks>
/// Tests that commit a <see cref="DropGraphOperation"/> against a backend store are
/// deferred to slice-specific tests (<c>Repository.InMemory</c>, <c>Repository.GraphDb</c>)
/// once those backends add dispatch support for the operation type.
/// </remarks>
[Collection("EntityOptions")]
public sealed class DropGraphOperationTests : IClassFixture<EntityOptionsFixture>
{
    private const string GraphIri = "https://forge-it.net/branches/feature-X";

    private static InMemoryEntityStore BuildStore()
    {
        var registry = new RdfMapperRegistry();
        var opts = Options.Create(new EntityRepositoryOptions());
        return new InMemoryEntityStore(registry, opts);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Structure
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DropGraphOperation_EntityIri_equals_GraphIri()
    {
        var op = new DropGraphOperation(GraphIri);
        op.EntityIri.ShouldBe(GraphIri);
        op.GraphIri.ShouldBe(GraphIri);
    }

    [Fact]
    public void DropGraphOperation_AspectIri_defaults_to_NoOp()
    {
        var op = new DropGraphOperation(GraphIri);
        op.AspectIri.ShouldBe(Forge.Aspects.Abstractions.Aspect.NoOpIri);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Constructor guards
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DropGraphOperation_ctor_throws_for_null_empty_or_whitespace(string? bad)
    {
        Should.Throw<ArgumentException>(() => new DropGraphOperation(bad!));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Builder — returns same instance (fluent)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DropGraph_returns_same_EntityTransaction_instance()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);

        var returned = tx.DropGraph(GraphIri);

        returned.ShouldBeSameAs(tx);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Builder guards
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DropGraph_throws_ArgumentException_for_null_empty_or_whitespace(string? bad)
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);

        Should.Throw<ArgumentException>(() => tx.DropGraph(bad!));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Builder state guards
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DropGraph_throws_after_commit()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.CommitAsync();

        Should.Throw<InvalidOperationException>(() => tx.DropGraph(GraphIri));
    }

    [Fact]
    public async Task DropGraph_throws_after_dispose()
    {
        await using var store = BuildStore();
        var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => tx.DropGraph(GraphIri));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Commit — InMemory backend executes DropGraphOperation
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DropGraph_removes_all_triples_for_graph_iri()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        // Seed an entity whose IRI will serve as the "graph IRI" to drop.
        var artist = new Artist { Name = "Aria Nova", Country = "no" };
        await store.SaveAsync(artist, WriteMode.Create);
        (await repo.FindAsync(artist.Iri)).ShouldNotBeNull();

        // Drop the entity's IRI as a graph — it should remove all subject-closure triples.
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.DropGraph(artist.Iri).CommitAsync();

        (await repo.FindAsync(artist.Iri)).ShouldBeNull();
    }

    [Fact]
    public async Task DropGraph_on_absent_iri_is_idempotent()
    {
        await using var store = BuildStore();

        const string absentIri = "https://forge-it.net/branches/absent";
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);

        // Should not throw even when no triples exist for the IRI.
        await tx.DropGraph(absentIri).CommitAsync();
    }

    [Fact]
    public async Task DropGraph_chains_with_Create_in_same_transaction()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        var existing = new Artist { Name = "Kai Storm", Country = "us" };
        await store.SaveAsync(existing, WriteMode.Create);

        var incoming = new Artist { Name = "Aria Nova", Country = "no" };

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx
            .DropGraph(existing.Iri)
            .Create(incoming)
            .CommitAsync();

        (await repo.FindAsync(existing.Iri)).ShouldBeNull();
        (await repo.FindAsync(incoming.Iri)).ShouldNotBeNull();
    }

    [Fact]
    public async Task DropGraph_rolls_back_when_later_operation_fails()
    {
        await using var store = BuildStore();
        var repo = new EntityRepository<Artist>(store);

        var existing = new Artist { Name = "Kai Storm", Country = "us" };
        await store.SaveAsync(existing, WriteMode.Create);

        // Two artists with the same identity produce the same IRI → second Create fails.
        var dup1 = new Artist { Name = "Same Identity", Country = "xx" };
        var dup2 = new Artist { Name = "Same Identity", Country = "xx" };

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.DropGraph(existing.Iri).Create(dup1).Create(dup2);

        await Should.ThrowAsync<EntityAlreadyExistsException>(async () => await tx.CommitAsync());

        // Rollback must restore the original entity.
        (await repo.FindAsync(existing.Iri)).ShouldNotBeNull();
    }

}
