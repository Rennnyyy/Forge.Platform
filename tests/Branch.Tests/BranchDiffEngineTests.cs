using Forge.Branch.Merge;
using Forge.Entity;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.Mapping;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Unit / integration tests for <see cref="BranchDiffEngine"/> using the InMemory
/// backend (scoped single-graph SPARQL path). See Branch ADR-0004.
/// </summary>
public sealed class BranchDiffEngineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string Source = "https://forge-it.net/branches/source";
    private const string Target = "https://forge-it.net/branches/target";

    private static (InMemoryEntityStore Store, RdfMapperRegistry Registry, BranchDiffEngine Engine)
        Build()
    {
        var registry = new RdfMapperRegistry();
        var repoOpts = Options.Create(new EntityRepositoryOptions
        {
            DefaultBranchIri = Source,
        });
        var store = new InMemoryEntityStore(registry, repoOpts);
        var engine = new BranchDiffEngine(registry, store, repoOpts);
        return (store, registry, engine);
    }

    private static Branch MakeBranch(string name) =>
        new() { Name = name, CreatedAt = DateTimeOffset.UtcNow };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeDiffAsync_returns_empty_when_source_graph_is_empty()
    {
        var (_, _, engine) = Build();

        var delta = await engine.ComputeDiffAsync(Source, Target);

        delta.IsEmpty.ShouldBeTrue();
        delta.SourceGraphIri.ShouldBe(Source);
        delta.TargetGraphIri.ShouldBe(Target);
    }

    [Fact]
    public async Task ComputeDiffAsync_returns_Added_for_entity_in_source_only()
    {
        var (store, _, engine) = Build();

        using (BranchScope.Use(Source))
            await store.SaveAsync(MakeBranch("feature-x"));

        var delta = await engine.ComputeDiffAsync(Source, Target);

        delta.IsEmpty.ShouldBeFalse();
        delta.Entries.Count.ShouldBe(1);
        delta.Entries[0].Kind.ShouldBe(EntityDeltaKind.Added);
    }

    [Fact]
    public async Task ComputeDiffAsync_returns_Modified_for_entity_in_both_graphs()
    {
        var (store, _, engine) = Build();
        var branch = MakeBranch("shared");

        using (BranchScope.Use(Source))
            await store.SaveAsync(branch);
        // Save the same branch IRI to the target graph; the diff detects IRI presence, not property delta.
        using (BranchScope.Use(Target))
            await store.SaveAsync(branch);

        var delta = await engine.ComputeDiffAsync(Source, Target);

        delta.Entries.Count.ShouldBe(1);
        delta.Entries[0].Kind.ShouldBe(EntityDeltaKind.Modified);
        delta.Entries[0].EntityIri.ShouldBe(branch.Iri);
    }

    [Fact]
    public async Task ComputeDiffAsync_ignores_target_only_entities()
    {
        var (store, _, engine) = Build();

        // Only save in target — source is empty.
        using (BranchScope.Use(Target))
            await store.SaveAsync(MakeBranch("target-only"));

        var delta = await engine.ComputeDiffAsync(Source, Target);

        delta.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task ComputeDiffAsync_handles_added_and_modified_entities_together()
    {
        var (store, _, engine) = Build();
        var existing = MakeBranch("existing");
        var newBranch = MakeBranch("new-branch");

        // existing is in both graphs; new-branch is only in source.
        using (BranchScope.Use(Source))
        {
            await store.SaveAsync(existing);
            await store.SaveAsync(newBranch);
        }
        using (BranchScope.Use(Target))
            await store.SaveAsync(existing);

        var delta = await engine.ComputeDiffAsync(Source, Target);

        delta.Entries.Count.ShouldBe(2);
        delta.Entries.ShouldContain(e => e.Kind == EntityDeltaKind.Added
                                         && e.EntityIri == newBranch.Iri);
        delta.Entries.ShouldContain(e => e.Kind == EntityDeltaKind.Modified
                                         && e.EntityIri == existing.Iri);
    }

    [Fact]
    public async Task ComputeDiffAsync_entry_TypeIri_can_be_resolved_to_Branch_mapper()
    {
        var (store, registry, engine) = Build();
        var branch = MakeBranch("type-check");

        using (BranchScope.Use(Source))
            await store.SaveAsync(branch);

        var delta = await engine.ComputeDiffAsync(Source, Target);

        delta.Entries.Count.ShouldBe(1);
        var opts = new EntityRepositoryOptions();
        // The TypeIri must resolve to some mapper — at minimum a ReflectionRdfMapper<Branch>.
        var mapper = registry.ForTypeIri(delta.Entries[0].TypeIri, opts);
        mapper.ShouldNotBeNull();
    }

    [Fact]
    public async Task ComputeDiffAsync_deduplicates_inherited_Snapshot_to_most_derived_type()
    {
        var (store, registry, engine) = Build();
        var snapshot = new Snapshot
        {
            Name = "snap-1",
            SnapshotAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Snapshot inherits Branch. Two rdf:type triples are written: branches/snapshot
        // and branches. The engine should keep only the most-derived type IRI.
        using (BranchScope.Use(Source))
            await store.SaveAsync(snapshot);

        var delta = await engine.ComputeDiffAsync(Source, Target);

        // One entry — not two.
        delta.Entries.Where(e => e.EntityIri == snapshot.Iri)
            .Count()
            .ShouldBe(1);
    }
}
