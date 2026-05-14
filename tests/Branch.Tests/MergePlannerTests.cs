using Forge.Branch.Merge;
using Forge.Entity;
using Forge.Entity.Tests.Fixtures.Sample;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.Mapping;
using Forge.Repository.Transaction;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Unit tests for <see cref="MergePlanner"/>. See Branch ADR-0006.
/// </summary>
public sealed class MergePlannerTests
{
    private const string Source = "https://forge-it.net/branches/source";
    private const string Target = "https://forge-it.net/branches/target";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (InMemoryEntityStore Store, MergePlanner Planner, RdfMapperRegistry Registry, EntityRepositoryOptions RepoOpts) Build()
    {
        var registry = new RdfMapperRegistry();
        var repoOpts = new EntityRepositoryOptions { DefaultBranchIri = Source };
        var store = new InMemoryEntityStore(registry, Options.Create(repoOpts));
        var planner = new MergePlanner(registry, Options.Create(repoOpts));
        return (store, planner, registry, repoOpts);
    }

    private static Branch MakeBranch(string name) =>
        new() { Name = name, CreatedAt = DateTimeOffset.UtcNow };

    /// <summary>
    /// Builds an <see cref="EntityGraphDelta"/> using the shared registry + options
    /// so the planner can resolve type IRIs back to mappers.
    /// </summary>
    private static EntityGraphDelta BuildDelta(
        RdfMapperRegistry registry,
        EntityRepositoryOptions opts,
        IEnumerable<(IEntity Entity, EntityDeltaKind Kind)> entries)
    {
        var deltaEntries = entries.Select(e =>
        {
            var mapper = registry.ForEntityType(e.Entity.GetType());
            var typeIri = mapper.ResolveTypeIri(opts);
            return new EntityDeltaEntry(e.Entity.Iri, typeIri, e.Kind);
        }).ToList();

        return new EntityGraphDelta(Source, Target, deltaEntries);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanAsync_returns_empty_list_for_empty_delta()
    {
        var (store, planner, _, _) = Build();
        var delta = new EntityGraphDelta(Source, Target, []);

        var ops = await planner.PlanAsync(delta, store, store);

        ops.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlanAsync_produces_CreateOperation_when_entity_absent_from_target()
    {
        var (store, planner, registry, opts) = Build();
        var branch = MakeBranch("new-branch");

        // Entity exists in source only.
        using (BranchScope.Use(Source))
            await store.SaveAsync(branch);

        var delta = BuildDelta(registry, opts, [(branch, EntityDeltaKind.Added)]);
        var ops = await planner.PlanAsync(delta, store, store);

        ops.Count.ShouldBe(1);
        ops[0].ShouldBeOfType<CreateOperation<Branch>>();
    }

    [Fact]
    public async Task PlanAsync_produces_UpdateOperation_when_entity_exists_in_target()
    {
        var (store, planner, registry, opts) = Build();
        var branch = MakeBranch("existing");

        // Entity exists in both graphs.
        using (BranchScope.Use(Source))
            await store.SaveAsync(branch);
        using (BranchScope.Use(Target))
            await store.SaveAsync(branch);

        var delta = BuildDelta(registry, opts, [(branch, EntityDeltaKind.Modified)]);
        var ops = await planner.PlanAsync(delta, store, store);

        ops.Count.ShouldBe(1);
        ops[0].ShouldBeOfType<UpdateOperation<Branch>>();
    }

    [Fact]
    public async Task PlanAsync_mixes_CreateOperation_and_UpdateOperation_correctly()
    {
        var (store, planner, registry, opts) = Build();
        var existing = MakeBranch("existing");
        var newBranch = MakeBranch("new");

        using (BranchScope.Use(Source))
        {
            await store.SaveAsync(existing);
            await store.SaveAsync(newBranch);
        }
        using (BranchScope.Use(Target))
            await store.SaveAsync(existing);

        var delta = BuildDelta(registry, opts, [
            (existing, EntityDeltaKind.Modified),
            (newBranch, EntityDeltaKind.Added),
        ]);
        var ops = await planner.PlanAsync(delta, store, store);

        ops.Count.ShouldBe(2);
        ops.ShouldContain(op => op is CreateOperation<Branch>);
        ops.ShouldContain(op => op is UpdateOperation<Branch>);
    }

    [Fact]
    public async Task PlanAsync_throws_MergePlanHydrationException_when_entity_missing_from_source()
    {
        var (store, planner, registry, opts) = Build();
        var branch = MakeBranch("ghost");
        // Intentionally NOT saved to source — entity IRI exists only in the delta.
        // Prime the registry so the planner can resolve the type (hydration failure comes later).
        var typeIri = registry.ForEntityType(typeof(Branch)).ResolveTypeIri(opts);
        var delta = new EntityGraphDelta(Source, Target, [
            new EntityDeltaEntry(branch.Iri, typeIri, EntityDeltaKind.Added),
        ]);

        await Should.ThrowAsync<MergePlanHydrationException>(
            () => planner.PlanAsync(delta, store, store));
    }

    [Fact]
    public async Task PlanAsync_throws_MergePlanUnresolvableTypeException_for_unknown_type_iri()
    {
        var (store, planner, _, _) = Build();
        var branch = MakeBranch("any");
        using (BranchScope.Use(Source))
            await store.SaveAsync(branch);

        var delta = new EntityGraphDelta(Source, Target, [
            new EntityDeltaEntry(branch.Iri, "https://example.com/types/unknown", EntityDeltaKind.Added),
        ]);

        await Should.ThrowAsync<MergePlanUnresolvableTypeException>(
            () => planner.PlanAsync(delta, store, store));
    }

    [Fact]
    public async Task PlanAsync_orders_Album_after_Artist_due_to_owning_collection()
    {
        var (store, planner, registry, opts) = Build();

        // Artist has no outgoing owning refs.
        // Album [Owning("hasArtist")] → Artist, so Album depends on Artist.
        var artist = new Artist { Name = "Aria Nova", Country = "US" };
        var album = new Album { Title = "Debut", ReleaseYear = 2025 };

        using (BranchScope.Use(Source))
        {
            await store.SaveAsync(artist);
            await album.Artists.AddAsync(artist);
            await store.SaveAsync(album);
        }

        var delta = BuildDelta(registry, opts, [
            (artist, EntityDeltaKind.Added),
            (album, EntityDeltaKind.Added),
        ]);

        var ops = await planner.PlanAsync(delta, store, store);

        ops.Count.ShouldBe(2);
        var artistIndex = ops.ToList().FindIndex(op =>
            op is EntityWriteOperation w && w.EntityIri == artist.Iri);
        var albumIndex = ops.ToList().FindIndex(op =>
            op is EntityWriteOperation w && w.EntityIri == album.Iri);

        // Artist must come before Album.
        artistIndex.ShouldBeLessThan(albumIndex);
    }
}
