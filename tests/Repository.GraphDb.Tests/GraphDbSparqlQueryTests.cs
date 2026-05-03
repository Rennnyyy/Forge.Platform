using Forge.Entity;
using Forge.Repository;
using Forge.Repository.Mapping;
using Forge.Sparql;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Repository.GraphDb.Tests;

/// <summary>
/// Integration tests for <see cref="GraphDbEntityStore"/> implementing
/// <see cref="ISparqlQueryStore"/>. Validates that SPARQL SELECT queries — whether
/// hand-authored or emitted by the LINQ-to-SPARQL provider — execute correctly against
/// a live Ontotext GraphDB instance.
/// <para>
/// Every test skips gracefully when <see cref="GraphDbFixture.Available"/> is
/// <c>false</c> (no container runtime available or Podman/Docker daemon not running).
/// Bring GraphDB up with:
/// <code>podman compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d</code>
/// </para>
/// </summary>
[Collection("GraphDb")]
[Trait("Category", "Integration")]
[Trait("Backend", "GraphDB")]
public sealed class GraphDbSparqlQueryTests
{
    private readonly GraphDbFixture _fx;
    public GraphDbSparqlQueryTests(GraphDbFixture fx) => _fx = fx;

    private const string SkipMessage =
        "GraphDB not reachable — start: " +
        "podman compose -f tests/Entity.Repository.GraphDb.Tests/docker-compose.graphdb.yml up -d";

    private GraphDbEntityStore BuildStore()
    {
        var registry = new RdfMapperRegistry();
        var repoOpts = Options.Create(new EntityRepositoryOptions());
        var gdbOpts  = Options.Create(new GraphDbOptions
        {
            BaseUrl      = _fx.BaseUrl,
            RepositoryId = _fx.RepositoryId,
            Timeout      = TimeSpan.FromSeconds(15),
        });
        return new GraphDbEntityStore(new HttpClient(), registry, repoOpts, gdbOpts);
    }

    private async Task<GraphDbEntityStore> BuildPopulatedStoreAsync()
    {
        var store = BuildStore();
        await store.SaveAsync(new Artist { Name = "Aria",  Country = "us", Active = true,  DebutYear = 2010 });
        await store.SaveAsync(new Artist { Name = "Bjorn", Country = "se", Active = false, DebutYear = 2005 });
        await store.SaveAsync(new Artist { Name = "Cleo",  Country = "us", Active = true,  DebutYear = 2018, Bio = "soulful" });
        await store.SaveAsync(new Artist { Name = "Dora",  Country = "de", Active = true,  DebutYear = 2001 });
        return store;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Raw ExecuteSelectAsync — verifies HTTP round-trip and binding parsing
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task ExecuteSelectAsync_raw_sparql_returns_iri_and_literal_bindings()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        // Use dynamically resolved IRIs so the test is baseline-IRI agnostic.
        var typeIri = new EntityRepositoryOptions().ResolveTypeIri("Artist", "artists");
        var nameIri = $"{EntityOptions.Current.PredicateBaseIri.TrimEnd('/')}/artist/name";

        var sparql = $@"
SELECT ?s ?name WHERE {{
    ?s a <{typeIri}> .
    ?s <{nameIri}> ?name .
}}";
        var rows = new List<SparqlResultRow>();
        await foreach (var row in ((ISparqlQueryStore)store).ExecuteSelectAsync(sparql))
            rows.Add(row);

        rows.Count.ShouldBe(4);
        // ?s is bound as an IRI term.
        rows.All(r => r.GetIri("s") is not null).ShouldBeTrue();
        // ?name is bound as a literal term.
        rows.Select(r => r.GetLiteral("name")).ShouldBe(
            new[] { "Aria", "Bjorn", "Cleo", "Dora" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. LINQ provider — equality filter on [Predicate] property
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Where_equality_filter_returns_matching_entities()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var us = await store.Query<Artist>()
            .Where(a => a.Country == "us")
            .ToListAsync();

        us.Count.ShouldBe(2);
        us.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. LINQ provider — combined boolean filter (&& + bare bool member access)
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Where_combined_boolean_filters_return_matching_entities()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var activeUS = await store.Query<Artist>()
            .Where(a => a.Country == "us" && a.Active)
            .ToListAsync();

        activeUS.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. LINQ provider — numeric comparison on a [Predicate] property
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Where_numeric_comparison_returns_matching_entities()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var debutAfter2009 = await store.Query<Artist>()
            .Where(a => a.DebutYear > 2009)
            .ToListAsync();

        debutAfter2009.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. LINQ provider — string.StartsWith translates to STRSTARTS
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task String_StartsWith_translates_to_STRSTARTS_filter()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var result = await store.Query<Artist>()
            .Where(a => a.Name.StartsWith("B"))
            .ToListAsync();

        result.Single().Name.ShouldBe("Bjorn");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. LINQ provider — null / not-null check translates to BOUND / !BOUND
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Null_check_translates_to_BOUND_filter()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var withBio    = await store.Query<Artist>().Where(a => a.Bio != null).ToListAsync();
        var withoutBio = await store.Query<Artist>().Where(a => a.Bio == null).ToListAsync();

        withBio.Single().Name.ShouldBe("Cleo");
        withoutBio.Count.ShouldBe(3);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. LINQ provider — OrderBy + Take applies ORDER BY + LIMIT server-side
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task OrderBy_and_Take_apply_ordering_and_limit()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var firstTwoByYear = await store.Query<Artist>()
            .OrderBy(a => a.DebutYear)
            .Take(2)
            .ToListAsync();

        firstTwoByYear.Select(a => a.Name).ShouldBe(new[] { "Dora", "Bjorn" });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. LINQ provider — CountAsync returns count without materializing entities
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task CountAsync_returns_matching_count()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var totalActive = await store.Query<Artist>().CountAsync(a => a.Active);
        var totalAll    = await store.Query<Artist>().CountAsync();

        totalActive.ShouldBe(3);
        totalAll.ShouldBe(4);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. LINQ provider — AnyAsync with and without predicate
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task AnyAsync_returns_existence()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        (await store.Query<Artist>().AnyAsync(a => a.Country == "se")).ShouldBeTrue();
        (await store.Query<Artist>().AnyAsync(a => a.Country == "jp")).ShouldBeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 10. LINQ provider — FirstOrDefaultAsync returns null when nothing matches
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task FirstOrDefaultAsync_returns_null_when_no_match()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var none = await store.Query<Artist>().Where(a => a.Country == "jp").FirstOrDefaultAsync();
        var some = await store.Query<Artist>().OrderBy(a => a.Name).FirstOrDefaultAsync();

        none.ShouldBeNull();
        some.ShouldNotBeNull();
        some!.Name.ShouldBe("Aria");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 11. LINQ provider — Skip + Take applies OFFSET + LIMIT
    // ════════════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task Skip_and_Take_apply_offset_and_limit()
    {
        Skip.If(!_fx.Available, SkipMessage);
        await _fx.ClearAsync();
        await using var store = await BuildPopulatedStoreAsync();

        var page = await store.Query<Artist>()
            .OrderBy(a => a.Name)
            .Skip(1).Take(2)
            .ToListAsync();

        page.Select(a => a.Name).ShouldBe(new[] { "Bjorn", "Cleo" });
    }
}
