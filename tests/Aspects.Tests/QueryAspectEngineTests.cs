using Forge.Entity;
using Forge.Aspects;
using Forge.Aspects.DependencyInjection;
using Forge.Aspects.Operation;
using Forge.Aspects.Query;
using Forge.Repository;
using Forge.Repository.DependencyInjection;
using Forge.Repository.InMemory.DependencyInjection;
using Forge.Sparql;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Aspects.Tests;

/// <summary>
/// Tests for the read-aspect pipeline (ADR-0007):
/// <list type="bullet">
///   <item>Access gate via <see cref="IQueryAspect.FilterWhere"/> on LoadAsync and QueryByTypeAsync.</item>
///   <item>Output shape via <see cref="IQueryAspect.ResultShapeTtl"/> on LoadAsync.</item>
///   <item>Scope isolation via <see cref="QueryAspectScope"/>.</item>
///   <item>Unit-level: <see cref="IQueryAspectEngine.InjectFilter"/> and <see cref="IQueryAspectEngine.InjectFilterDynamic"/>.</item>
/// </list>
/// </summary>
[Collection("EntityOptions")]
public sealed class QueryAspectEngineTests : IClassFixture<EntityOptionsFixture>
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
    // QA-1: No scope → LoadAsync returns entity unchanged
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_without_read_aspect_scope_returns_entity_normally()
    {
        await using var sp = BuildProvider();
        var store = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Bach", "de");
        await store.SaveAsync(artist, WriteMode.Create);

        var result = await store.LoadAsync<Artist>(artist.Iri);
        result.ShouldNotBeNull();
        result!.Name.ShouldBe("Bach");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-2: FilterWhere that always grants → LoadAsync returns entity
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_with_matching_FilterWhere_returns_entity()
    {
        // FilterWhere always produces a row → access granted.
        var aspect = new InlineTtlQueryAspect("always-grant", "BIND(true AS ?_ok)", null);
        await using var sp = BuildProvider(s => s.AddQueryAspect(aspect));
        var store = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Mozart", "at");
        await store.SaveAsync(artist, WriteMode.Create);

        using var _ = QueryAspectScope.Use(aspect.Iri);
        var result = await store.LoadAsync<Artist>(artist.Iri);
        result.ShouldNotBeNull();
        result!.Name.ShouldBe("Mozart");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-3: FilterWhere that never grants → LoadAsync throws QueryAspectViolationException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_with_non_matching_FilterWhere_throws_QueryAspectViolationException()
    {
        // FilterWhere never produces a row → access denied.
        var aspect = new InlineTtlQueryAspect("always-deny", "FILTER(false)", null);
        await using var sp = BuildProvider(s => s.AddQueryAspect(aspect));
        var store = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Beethoven", "de");
        await store.SaveAsync(artist, WriteMode.Create);

        using var _ = QueryAspectScope.Use(aspect.Iri);

        var ex = await Should.ThrowAsync<QueryAspectViolationException>(
            () => store.LoadAsync<Artist>(artist.Iri).AsTask());

        ex.SourceAspectIri.ShouldBe("always-deny");
        ex.EntityIri.ShouldBe(artist.Iri);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-4: Disposing the scope restores unrestricted reads
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Disposing_scope_restores_unrestricted_reads()
    {
        var aspect = new InlineTtlQueryAspect("deny-all", "FILTER(false)", null);
        await using var sp = BuildProvider(s => s.AddQueryAspect(aspect));
        var store = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Brahms", "de");
        await store.SaveAsync(artist, WriteMode.Create);

        // Inside scope — denied
        using (QueryAspectScope.Use(aspect.Iri))
        {
            await Should.ThrowAsync<QueryAspectViolationException>(
                () => store.LoadAsync<Artist>(artist.Iri).AsTask());
        }

        // Outside scope — allowed
        var result = await store.LoadAsync<Artist>(artist.Iri);
        result.ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-5: ResultShapeTtl violation → LoadAsync throws QueryAspectViolationException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_with_failing_ResultShapeTtl_throws_QueryAspectViolationException()
    {
        var shapeTtl = @"
@prefix sh:    <http://www.w3.org/ns/shacl#> .
@prefix artist: <https://forge-it.net/predicates/artist/> .

<urn:shape:country-must-be-us>
    a sh:NodeShape ;
    sh:targetClass <https://forge-it.net/types/artists> ;
    sh:property [
        sh:path artist:country ;
        sh:pattern ""^us$"" ;
        sh:message ""Only US artists are accessible via this aspect."" ;
    ] .
";
        var aspect = new InlineTtlQueryAspect("us-only-shape", null, shapeTtl);
        await using var sp = BuildProvider(s => s.AddQueryAspect(aspect));
        var store = sp.GetRequiredService<IEntityStore>();

        // Schubert is "at"; shape demands "us" → violation.
        var artist = MakeArtist("Schubert", "at");
        await store.SaveAsync(artist, WriteMode.Create);

        using var _ = QueryAspectScope.Use(aspect.Iri);

        var ex = await Should.ThrowAsync<QueryAspectViolationException>(
            () => store.LoadAsync<Artist>(artist.Iri).AsTask());

        ex.SourceAspectIri.ShouldBe("us-only-shape");
        ex.Violations.ShouldNotBeNull();
        ex.Violations!.Count.ShouldBeGreaterThan(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-6: ResultShapeTtl passes when entity conforms
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_with_passing_ResultShapeTtl_returns_entity()
    {
        var shapeTtl = @"
@prefix sh:    <http://www.w3.org/ns/shacl#> .
@prefix artist: <https://forge-it.net/predicates/artist/> .

<urn:shape:country-must-be-us>
    a sh:NodeShape ;
    sh:targetClass <https://forge-it.net/types/artists> ;
    sh:property [
        sh:path artist:country ;
        sh:pattern ""^us$"" ;
        sh:message ""Only US artists are accessible via this aspect."" ;
    ] .
";
        var aspect = new InlineTtlQueryAspect("us-only-shape-qa6", null, shapeTtl);
        await using var sp = BuildProvider(s => s.AddQueryAspect(aspect));
        var store = sp.GetRequiredService<IEntityStore>();

        var artist = MakeArtist("Hanson", "us");
        await store.SaveAsync(artist, WriteMode.Create);

        using var _ = QueryAspectScope.Use(aspect.Iri);
        var result = await store.LoadAsync<Artist>(artist.Iri);
        result.ShouldNotBeNull();
        result!.Country.ShouldBe("us");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-7: QueryByTypeAsync without scope returns all entities
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryByTypeAsync_without_scope_returns_all_entities()
    {
        await using var sp = BuildProvider();
        var store = sp.GetRequiredService<IEntityStore>();

        var a1 = MakeArtist("A-one", "us");
        var a2 = MakeArtist("A-two", "gb");
        await store.SaveAsync(a1, WriteMode.Create);
        await store.SaveAsync(a2, WriteMode.Create);

        var results = await store.QueryByTypeAsync<Artist>().ToListAsync();
        results.Count.ShouldBe(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-8: QueryByTypeAsync with no-filter scope (only ResultShapeTtl) validates aggregate
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryByTypeAsync_with_failing_aggregate_ResultShapeTtl_throws()
    {
        var shapeTtl = @"
@prefix sh:    <http://www.w3.org/ns/shacl#> .
@prefix artist: <https://forge-it.net/predicates/artist/> .

<urn:shape:all-us>
    a sh:NodeShape ;
    sh:targetClass <https://forge-it.net/types/artists> ;
    sh:property [
        sh:path artist:country ;
        sh:pattern ""^us$"" ;
        sh:message ""All result artist must be from US."" ;
    ] .
";
        var aspect = new InlineTtlQueryAspect("all-us-shape", null, shapeTtl);
        await using var sp = BuildProvider(s => s.AddQueryAspect(aspect));
        var store = sp.GetRequiredService<IEntityStore>();

        // One US artist and one non-US; shape demands all be "us" → aggregate violation.
        await store.SaveAsync(MakeArtist("US-Artist", "us"), WriteMode.Create);
        await store.SaveAsync(MakeArtist("GB-Artist", "gb"), WriteMode.Create);

        using var _ = QueryAspectScope.Use(aspect.Iri);

        // Enumerate all, then the ValidateResultGraph call fires after the scan.
        var ex = await Should.ThrowAsync<QueryAspectViolationException>(async () =>
        {
            await foreach (var _ in store.QueryByTypeAsync<Artist>()) { }
        });

        ex.SourceAspectIri.ShouldBe("all-us-shape");
        ex.Violations.ShouldNotBeNull();
        ex.Violations!.Count.ShouldBeGreaterThan(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-9: InjectFilter appends WHERE fragment before closing brace
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectFilter_appends_fragment_before_closing_brace()
    {
        var engine = new QueryAspectEngine(new ShapeCache());
        var aspect = new InlineTtlQueryAspect("test", "FILTER(?x = 1)", null);

        const string query = "SELECT ?s WHERE { ?s a <urn:Type> . }";
        var result = engine.InjectFilter(query, aspect);

        result.ShouldContain("FILTER(?x = 1)");
        result.ShouldEndWith("}");
        // Original content preserved
        result.ShouldContain("?s a <urn:Type>");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-10: InjectFilter is no-op when FilterWhere is null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectFilter_is_noop_when_FilterWhere_is_null()
    {
        var engine = new QueryAspectEngine(new ShapeCache());
        var aspect = new InlineTtlQueryAspect("no-filter", null, null);

        const string query = "SELECT ?s WHERE { ?s a <urn:Type> . }";
        engine.InjectFilter(query, aspect).ShouldBe(query);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-11: InjectFilterDynamic substitutes ##aspect:filter## placeholder
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectFilterDynamic_substitutes_placeholder()
    {
        var engine = new QueryAspectEngine(new ShapeCache());
        var aspect = new InlineTtlQueryAspect("test", "FILTER(?x = 1)", null);

        const string query = "SELECT ?s WHERE { ?s a <urn:Type> . ##aspect:filter## }";
        var result = engine.InjectFilterDynamic(query, aspect);

        result.ShouldContain("FILTER(?x = 1)");
        result.ShouldNotContain("##aspect:filter##");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-12: InjectFilterDynamic throws when FilterWhere set but placeholder absent
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectFilterDynamic_throws_when_filter_set_but_placeholder_absent()
    {
        var engine = new QueryAspectEngine(new ShapeCache());
        var aspect = new InlineTtlQueryAspect("test", "FILTER(?x = 1)", null);

        const string query = "SELECT ?s WHERE { ?s a <urn:Type> . }";

        var ex = Should.Throw<QueryAspectViolationException>(
            () => engine.InjectFilterDynamic(query, aspect));

        ex.SourceAspectIri.ShouldBe("test");
        ex.Message.ShouldContain("##aspect:filter##");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-13: InjectFilterDynamic removes placeholder when no FilterWhere set
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectFilterDynamic_removes_placeholder_when_no_filter()
    {
        var engine = new QueryAspectEngine(new ShapeCache());
        var aspect = new InlineTtlQueryAspect("no-filter", null, null);

        const string query = "SELECT ?s WHERE { ?s a <urn:Type> . ##aspect:filter## }";
        var result = engine.InjectFilterDynamic(query, aspect);

        result.ShouldNotContain("##aspect:filter##");
        result.ShouldContain("?s a <urn:Type>");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-14: LINQ Query<T>() works through the aspect decorator
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinqQuery_without_scope_returns_all_matching_entities()
    {
        await using var sp = BuildProvider();
        var store = sp.GetRequiredService<IEntityStore>();

        await store.SaveAsync(MakeArtist("Aria", "us"), WriteMode.Create);
        await store.SaveAsync(MakeArtist("Bjorn", "se"), WriteMode.Create);

        // No aspect scope — Query<T> should be forwarded through the decorator.
        var results = await store.Query<Artist>().ToListAsync();
        results.Count.ShouldBe(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QA-15: LINQ Query<T>() with aspect FilterWhere filters SPARQL results
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinqQuery_with_FilterWhere_aspect_restricts_results_via_injected_sparql()
    {
        await using var sp = BuildProvider();
        var store = sp.GetRequiredService<IEntityStore>();

        await store.SaveAsync(MakeArtist("Aria",  "us"), WriteMode.Create);
        await store.SaveAsync(MakeArtist("Bjorn", "se"), WriteMode.Create);
        await store.SaveAsync(MakeArtist("Cleo",  "us"), WriteMode.Create);

        // FilterWhere uses ?entityIri — the same convention as the per-entity access gate.
        const string usCountryFilter =
            "?entityIri <https://forge-it.net/predicates/artist/country> " +
            "\"us\"^^<http://www.w3.org/2001/XMLSchema#string> .";

        var aspect = new InlineTtlQueryAspect("us-only-linq", usCountryFilter, null);
        await using var sp2 = BuildProvider(s => s.AddQueryAspect(aspect));
        var store2 = sp2.GetRequiredService<IEntityStore>();
        await store2.SaveAsync(MakeArtist("Aria",  "us"), WriteMode.Create);
        await store2.SaveAsync(MakeArtist("Bjorn", "se"), WriteMode.Create);
        await store2.SaveAsync(MakeArtist("Cleo",  "us"), WriteMode.Create);

        using var _ = QueryAspectScope.Use(aspect.Iri);
        var results = await store2.Query<Artist>().ToListAsync();

        results.Count.ShouldBe(2);
        results.Select(a => a.Name).ShouldBe(new[] { "Aria", "Cleo" }, ignoreOrder: true);
    }
}
