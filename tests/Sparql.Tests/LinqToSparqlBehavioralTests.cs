using Forge.Entity;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Sparql.Tests;

/// <summary>
/// End-to-end behavioral tests for the LINQ-to-SPARQL provider against the in-memory
/// back-end. Covers the v1 surface declared in
/// <c>src/Entity.Sparql/adr/0001-linq-to-sparql-provider.md</c>.
/// </summary>
[Collection("EntityOptions")]
public sealed class LinqToSparqlBehavioralTests : IClassFixture<EntityOptionsFixture>
{
    private static InMemoryEntityStore BuildStore()
    {
        var registry = new RdfMapperRegistry();
        var opts = Options.Create(new EntityRepositoryOptions());
        return new InMemoryEntityStore(registry, opts);
    }

    private static async Task<InMemoryEntityStore> BuildPopulatedStoreAsync()
    {
        var store = BuildStore();
        await store.SaveAsync(new Artist { Name = "Aria",  Country = "us", Active = true,  DebutYear = 2010 });
        await store.SaveAsync(new Artist { Name = "Bjorn", Country = "se", Active = false, DebutYear = 2005 });
        await store.SaveAsync(new Artist { Name = "Cleo",  Country = "us", Active = true,  DebutYear = 2018, Bio = "soulful" });
        await store.SaveAsync(new Artist { Name = "Dora",  Country = "de", Active = true,  DebutYear = 2001 });
        return store;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Where + ToListAsync — equality filter on a [Predicate] property
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Where_equality_filter_returns_matching_entities()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var us = await store.Query<Artist>()
            .Where(a => a.Country == "us")
            .ToListAsync();

        us.Count.ShouldBe(2);
        us.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Combined boolean filters (&& and bare-bool member access)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Where_combined_boolean_filters_return_matching_entities()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var activeUS = await store.Query<Artist>()
            .Where(a => a.Country == "us" && a.Active)
            .ToListAsync();

        activeUS.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Numeric comparisons on a [Predicate] property
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Where_numeric_comparison_returns_matching_entities()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var debutAfter2009 = await store.Query<Artist>()
            .Where(a => a.DebutYear > 2009)
            .ToListAsync();

        debutAfter2009.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. string.Contains / StartsWith / EndsWith translate to SPARQL functions
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task String_predicates_are_translated_to_sparql_functions()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var startsWithB = await store.Query<Artist>()
            .Where(a => a.Name.StartsWith("B"))
            .ToListAsync();

        startsWithB.Single().Name.ShouldBe("Bjorn");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Null check translates to !BOUND / BOUND
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Null_check_translates_to_bound_and_filters_correctly()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var withBio = await store.Query<Artist>()
            .Where(a => a.Bio != null)
            .ToListAsync();

        withBio.Single().Name.ShouldBe("Cleo");

        var withoutBio = await store.Query<Artist>()
            .Where(a => a.Bio == null)
            .ToListAsync();

        withoutBio.Count.ShouldBe(3);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. OrderBy + Take applies ordering and limit server-side
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OrderBy_and_Take_apply_ordering_and_limit()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var firstTwoByYear = await store.Query<Artist>()
            .OrderBy(a => a.DebutYear)
            .Take(2)
            .ToListAsync();

        firstTwoByYear.Select(a => a.Name).ShouldBe(new[] { "Dora", "Bjorn" });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. CountAsync returns the matching-row count without materialization
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CountAsync_returns_matching_count()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var totalActive = await store.Query<Artist>().CountAsync(a => a.Active);
        totalActive.ShouldBe(3);

        var totalAll = await store.Query<Artist>().CountAsync();
        totalAll.ShouldBe(4);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. AnyAsync — with and without predicate
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AnyAsync_returns_existence()
    {
        await using var store = await BuildPopulatedStoreAsync();

        (await store.Query<Artist>().AnyAsync(a => a.Country == "us")).ShouldBeTrue();
        (await store.Query<Artist>().AnyAsync(a => a.Country == "fr")).ShouldBeFalse();
        (await store.Query<Artist>().AnyAsync()).ShouldBeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. FirstOrDefaultAsync returns null for empty match, value for non-empty
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FirstOrDefaultAsync_returns_null_or_value()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var none = await store.Query<Artist>()
            .Where(a => a.Country == "fr")
            .FirstOrDefaultAsync();
        none.ShouldBeNull();

        var some = await store.Query<Artist>()
            .OrderBy(a => a.Name)
            .FirstOrDefaultAsync();
        some.ShouldNotBeNull();
        some!.Name.ShouldBe("Aria");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 10. SingleAsync throws on multiple matches
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SingleAsync_throws_on_multiple_matches()
    {
        await using var store = await BuildPopulatedStoreAsync();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            store.Query<Artist>().SingleAsync(a => a.Country == "us").AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 11. Skip + Take applies OFFSET + LIMIT
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Skip_and_Take_apply_offset_and_limit()
    {
        await using var store = await BuildPopulatedStoreAsync();

        var page = await store.Query<Artist>()
            .OrderBy(a => a.Name)
            .Skip(1).Take(2)
            .ToListAsync();

        page.Select(a => a.Name).ShouldBe(new[] { "Bjorn", "Cleo" });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 12. Iri equality filter selects exactly the requested subject
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Iri_equality_filter_selects_exact_subject()
    {
        await using var store = await BuildPopulatedStoreAsync();
        var anyArtist = await store.Query<Artist>().FirstAsync();

        var matched = await store.Query<Artist>()
            .Where(a => a.Iri == anyArtist.Iri)
            .ToListAsync();

        matched.Single().Iri.ShouldBe(anyArtist.Iri);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 13. Non-SPARQL store throws NotSupportedException with a helpful message
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Query_on_non_sparql_store_throws_NotSupportedException()
    {
        var store = new NonSparqlStubStore();
        var ex = Should.Throw<NotSupportedException>(() => store.Query<Artist>());
        ex.Message.ShouldContain("ISparqlQueryStore");
    }

    private sealed class NonSparqlStubStore : IEntityStore
    {
        public string? NamedGraph => null;
        public ValueTask<T?> LoadAsync<T>(string iri, CancellationToken ct = default) where T : class, IEntity
            => throw new NotImplementedException();
        public ValueTask SaveAsync<T>(T entity, WriteMode mode = WriteMode.Replace, CancellationToken ct = default) where T : class, IEntity
            => throw new NotImplementedException();
        public ValueTask DeleteAsync(string iri, CancellationToken ct = default)
            => throw new NotImplementedException();
        public IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken ct = default) where T : class, IEntity
            => throw new NotImplementedException();
        public IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(string ownerIri, string predicate, CancellationToken ct = default)
            where T : class, IEntity
            => throw new NotImplementedException();
        public ValueTask DisposeAsync() => default;
    }
}
