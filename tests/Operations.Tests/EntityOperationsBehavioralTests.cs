using Forge.Entity;
using Forge.Repository;
using Forge.Repository.InMemory;
using Forge.Repository.Mapping;
using Forge.Sparql;
using Forge.Entity.Tests.Fixtures;
using Forge.Entity.Tests.Fixtures.Sample;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Forge.Operations.Tests;

/// <summary>
/// Behavioral spec for the <see cref="EntityOperations"/> ambient-store layer.
/// Tests are executed against the in-memory backend to avoid infrastructure dependencies.
///
/// <list type="bullet">
///   <item><c>CreateAsync</c> — persists a new entity and is readable back.</item>
///   <item><c>ReadAsync</c>  — returns null when absent.</item>
///   <item><c>UpdateAsync</c> — replaces a previously created entity.</item>
///   <item><c>DeleteAsync</c> — removes the entity.</item>
///   <item><c>ListAsync</c>  — enumerates all stored entities by type.</item>
///   <item>Scope isolation    — operations outside a <c>Use</c> scope throw.</item>
///   <item>Nested scopes      — inner scope overrides; outer scope restored on dispose.</item>
/// </list>
/// </summary>
[Collection("EntityOptions")]
public sealed class EntityOperationsBehavioralTests : IClassFixture<EntityOptionsFixture>
{
    private static InMemoryEntityStore BuildStore()
    {
        var registry = new RdfMapperRegistry();
        var opts = Options.Create(new EntityRepositoryOptions());
        return new InMemoryEntityStore(registry, opts);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. CreateAsync persists and ReadAsync retrieves
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAsync_persists_entity_and_ReadAsync_retrieves_it()
    {
        await using var store = BuildStore();
        using var _ = EntityOperations.Use(store);

        var artist = new Artist { Name = "Aria Nova", Country = "us" };
        await artist.CreateAsync();

        var loaded = await Artist.ReadAsync(artist.Iri);

        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Aria Nova");
        loaded.Country.ShouldBe("us");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. ReadAsync returns null for an absent IRI
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_returns_null_when_entity_is_absent()
    {
        await using var store = BuildStore();
        using var _ = EntityOperations.Use(store);

        var loaded = await Artist.ReadAsync("https://forge-it.net/artists/nonexistent");

        loaded.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. UpdateAsync replaces the entity
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateAsync_replaces_previously_created_entity()
    {
        await using var store = BuildStore();
        using var _ = EntityOperations.Use(store);

        var artist = new Artist { Name = "Kai Storm", Country = "de" };
        await artist.CreateAsync();

        // Modify a scalar and replace.
        artist.Bio = "Techno producer from Berlin.";
        await artist.UpdateAsync();

        var reloaded = await Artist.ReadAsync(artist.Iri);
        reloaded!.Bio.ShouldBe("Techno producer from Berlin.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. DeleteAsync removes the entity
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_removes_the_entity()
    {
        await using var store = BuildStore();
        using var _ = EntityOperations.Use(store);

        var artist = new Artist { Name = "Gone Artist", Country = "xx" };
        await artist.CreateAsync();

        await artist.DeleteAsync();

        var reloaded = await Artist.ReadAsync(artist.Iri);
        reloaded.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. ListAsync enumerates all stored entities of that type
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListAsync_returns_all_stored_entities_of_type()
    {
        await using var store = BuildStore();
        using var _ = EntityOperations.Use(store);

        var a1 = new Artist { Name = "Alice", Country = "us" };
        var a2 = new Artist { Name = "Bob", Country = "gb" };
        await a1.CreateAsync();
        await a2.CreateAsync();

        var all = new List<Artist>();
        await foreach (var a in Artist.ListAsync())
            all.Add(a);

        all.Count.ShouldBe(2);
        all.ShouldContain(a => a.Name == "Alice");
        all.ShouldContain(a => a.Name == "Bob");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. RequireStore throws when no store is bound
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RequireStore_throws_when_no_store_is_bound()
    {
        // Ensure no ambient store leaks in from other tests (AsyncLocal doesn't leak
        // across independent test tasks, but we guard explicitly here).
        EntityOperations.CurrentStore.ShouldBeNull();

        var artist = new Artist { Name = "Nobody", Country = "xx" };

        await Should.ThrowAsync<InvalidOperationException>(() => artist.CreateAsync().AsTask());
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. Nested Use scopes: inner overrides, outer restored on dispose
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Nested_scopes_restore_outer_store_on_dispose()
    {
        await using var outer = BuildStore();
        await using var inner = BuildStore();

        using var outerScope = EntityOperations.Use(outer);
        EntityOperations.CurrentStore.ShouldBeSameAs(outer);

        using (EntityOperations.Use(inner))
        {
            EntityOperations.CurrentStore.ShouldBeSameAs(inner);

            var artist = new Artist { Name = "Inner", Country = "us" };
            await artist.CreateAsync();
            (await Artist.ReadAsync(artist.Iri)).ShouldNotBeNull();
        }

        EntityOperations.CurrentStore.ShouldBeSameAs(outer);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. CurrentStore is null after scope disposed
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CurrentStore_is_null_after_scope_disposed()
    {
        await using var store = BuildStore();

        IDisposable scope = EntityOperations.Use(store);
        EntityOperations.CurrentStore.ShouldNotBeNull();

        scope.Dispose();
        EntityOperations.CurrentStore.ShouldBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. Query<T>() returns an IQueryable bound to the ambient store (ADR-0003)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_returns_an_IQueryable_bound_to_the_ambient_store()
    {
        await using var store = BuildStore();
        using var _ = EntityOperations.Use(store);

        await new Artist { Name = "Aurora", Country = "no", Active = true }.CreateAsync();
        await new Artist { Name = "Bjorn", Country = "se", Active = false }.CreateAsync();
        await new Artist { Name = "Cleo", Country = "us", Active = true }.CreateAsync();

        var actives = await EntityOperations.Query<Artist>()
            .Where(a => a.Active)
            .OrderBy(a => a.Name)
            .ToListAsync();

        actives.Select(a => a.Name).ShouldBe(new[] { "Aurora", "Cleo" });

        var count = await EntityOperations.Query<Artist>().CountAsync(a => a.Country == "se");
        count.ShouldBe(1);
    }
}
