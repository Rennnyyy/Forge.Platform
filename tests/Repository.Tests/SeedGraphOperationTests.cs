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
/// Behavioural spec for <see cref="SeedGraphOperation"/> and
/// <see cref="EntityTransaction.SeedFrom"/>. See Repository ADR-0004.
///
/// <list type="bullet">
///   <item>Structure — properties set correctly; <c>EntityIri</c> equals <c>TargetGraphIri</c>; <c>AspectIri</c> defaults to NoOp.</item>
///   <item>Constructor guards — rejects null/empty/whitespace source, target, and null/empty list.</item>
///   <item>Builder — <c>SeedFrom</c> returns the same instance (fluent).</item>
///   <item>Builder state guards — <c>SeedFrom</c> throws after commit and after dispose.</item>
///   <item>Multiple <c>SeedFrom</c> calls in one transaction are permitted.</item>
///   <item>InMemory backend copies entity triples from source to target graph atomically.</item>
///   <item>Missing entity IRI aborts the transaction with <see cref="SeedOperationMissingEntityException"/>; target graph left clean.</item>
/// </list>
/// </summary>
[Collection("EntityOptions")]
public sealed class SeedGraphOperationTests : IClassFixture<EntityOptionsFixture>
{
    private const string Source = "https://forge-it.net/branches/main";
    private const string Target = "https://forge-it.net/branches/feature-X";
    private static readonly IReadOnlyList<string> Iris =
        ["https://forge-it.net/artists/aria-nova"];

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
    public void SeedGraphOperation_properties_are_set_correctly()
    {
        var op = new SeedGraphOperation(Source, Target, Iris);

        op.SourceGraphIri.ShouldBe(Source);
        op.TargetGraphIri.ShouldBe(Target);
        op.EntityIris.ShouldBe(Iris);
    }

    [Fact]
    public void SeedGraphOperation_EntityIri_equals_TargetGraphIri()
    {
        var op = new SeedGraphOperation(Source, Target, Iris);
        op.EntityIri.ShouldBe(Target);
    }

    [Fact]
    public void SeedGraphOperation_AspectIri_defaults_to_NoOp()
    {
        var op = new SeedGraphOperation(Source, Target, Iris);
        op.AspectIri.ShouldBe(Forge.Aspects.Abstractions.Aspect.NoOpIri);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Constructor guards
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SeedGraphOperation_ctor_throws_for_null_empty_or_whitespace_sourceGraphIri(string? bad)
    {
        Should.Throw<ArgumentException>(() => new SeedGraphOperation(bad!, Target, Iris));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SeedGraphOperation_ctor_throws_for_null_empty_or_whitespace_targetGraphIri(string? bad)
    {
        Should.Throw<ArgumentException>(() => new SeedGraphOperation(Source, bad!, Iris));
    }

    [Fact]
    public void SeedGraphOperation_ctor_throws_for_null_entityIris()
    {
        Should.Throw<ArgumentNullException>(() => new SeedGraphOperation(Source, Target, null!));
    }

    [Fact]
    public void SeedGraphOperation_ctor_throws_for_empty_entityIris()
    {
        Should.Throw<ArgumentException>(() =>
            new SeedGraphOperation(Source, Target, Array.Empty<string>()));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Builder — returns same instance (fluent)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeedFrom_returns_same_EntityTransaction_instance()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);

        var returned = tx.SeedFrom(Source, Target, Iris);

        returned.ShouldBeSameAs(tx);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Builder state guards
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeedFrom_throws_after_commit()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.CommitAsync();

        Should.Throw<InvalidOperationException>(() => tx.SeedFrom(Source, Target, Iris));
    }

    [Fact]
    public async Task SeedFrom_throws_after_dispose()
    {
        await using var store = BuildStore();
        var tx = new EntityTransaction((ITransactionalEntityStore)store);
        await tx.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => tx.SeedFrom(Source, Target, Iris));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Multiple SeedFrom calls in one transaction are permitted
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Multiple_SeedFrom_calls_are_permitted_before_commit()
    {
        await using var store = BuildStore();
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);

        var iris2 = new[] { "https://forge-it.net/artists/kai-storm" };

        // Should not throw — enqueueing two seed operations is valid.
        var act = () =>
        {
            tx.SeedFrom(Source, Target, Iris);
            tx.SeedFrom(Source, "https://forge-it.net/branches/other", iris2);
        };
        act.ShouldNotThrow();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. InMemory backend supports SeedFrom
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InMemory_SeedFrom_copies_entity_triples_to_target_graph()
    {
        await using var store = BuildStore();

        // Arrange: save an entity in the source branch graph.
        var artist = new Artist { Name = "Aria Nova", Country = "se" };
        using (BranchScope.Use(Source))
            await store.SaveAsync(artist, WriteMode.Create);

        // Act: seed from source to target in a transaction.
        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.SeedFrom(Source, Target, [artist.Iri]);
        await tx.CommitAsync();

        // Assert: entity is readable from the target graph.
        Artist? seeded;
        using (BranchScope.Use(Target))
            seeded = await store.LoadAsync<Artist>(artist.Iri);

        seeded.ShouldNotBeNull();
        seeded!.Name.ShouldBe("Aria Nova");
        seeded.Country.ShouldBe("se");
    }

    [Fact]
    public async Task InMemory_SeedFrom_throws_SeedOperationMissingEntityException_when_iri_absent()
    {
        await using var store = BuildStore();
        var missingIri = "https://forge-it.net/artists/does-not-exist";

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.SeedFrom(Source, Target, [missingIri]);

        var ex = await Should.ThrowAsync<SeedOperationMissingEntityException>(
            () => tx.CommitAsync().AsTask());

        ex.SourceGraphIri.ShouldBe(Source);
        ex.MissingIris.ShouldContain(missingIri);
    }

    [Fact]
    public async Task InMemory_SeedFrom_does_not_modify_target_when_entity_is_missing()
    {
        await using var store = BuildStore();
        var presentArtist = new Artist { Name = "Kai Storm", Country = "de" };
        using (BranchScope.Use(Source))
            await store.SaveAsync(presentArtist, WriteMode.Create);

        var missingIri = "https://forge-it.net/artists/ghost";

        await using var tx = new EntityTransaction((ITransactionalEntityStore)store);
        tx.SeedFrom(Source, Target, [missingIri]);

        await Should.ThrowAsync<SeedOperationMissingEntityException>(
            () => tx.CommitAsync().AsTask());

        // Target graph must remain empty — no partial state written.
        Artist? shouldBeNull;
        using (BranchScope.Use(Target))
            shouldBeNull = await store.LoadAsync<Artist>(presentArtist.Iri);
        shouldBeNull.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. SeedOperationMissingEntityException — message and properties
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SeedOperationMissingEntityException_exposes_source_and_missing_iris()
    {
        var missing = new[] { "https://forge-it.net/artists/ghost", "https://forge-it.net/artists/missing" };
        var ex = new SeedOperationMissingEntityException(Source, missing);

        ex.SourceGraphIri.ShouldBe(Source);
        ex.MissingIris.ShouldBe(missing);
        ex.Message.ShouldContain(Source);
        ex.Message.ShouldContain("https://forge-it.net/artists/ghost");
    }
}
