using Forge.Entity;
using Forge.Entity.Tests.Sample;
using Shouldly;

namespace Forge.Entity.Tests;

/// <summary>
/// Draft tests showing how entities are defined and exercised.
/// They double as the executable spec the source generator must satisfy.
/// </summary>
public class EntityFixtureTests : IDisposable
{
    // Bind a per-instance ambient scope so parallel test runners do not race on the
    // global static BaseIri field. EntityOptionsInstance defaults to "https://forge-it.net".
    private readonly IDisposable _optionsScope =
        EntityOptions.Use(new EntityOptionsInstance());

    public void Dispose() => _optionsScope.Dispose();

    // -------------------------------------------------------------- Identity

    [Fact]
    public void Path_identity_is_materialized_from_identity_parts()
    {
        var foo = new Foo { Slug = "alpha" };

        foo.Iri.ShouldBe("https://forge-it.net/foos/alpha");
        foo.IsIdentitySealed.ShouldBeTrue();
    }

    [Fact]
    public void UuidV4_identity_is_assigned_at_construction()
    {
        var bar = new Bar { Name = "thing" };

        bar.IsIdentitySealed.ShouldBeTrue();
        bar.Iri.ShouldStartWith("https://forge-it.net/bars/");
        Guid.TryParse(bar.Iri["https://forge-it.net/bars/".Length..], out _).ShouldBeTrue();
    }

    [Fact]
    public void Identity_is_sealed_once_materialized()
    {
        var entity = new SealableTestEntity();
        entity.AssignIri("https://forge-it.net/foo/1");

        var ex = Should.Throw<InvalidOperationException>(
            () => entity.AssignIri("https://forge-it.net/foo/2"));
        ex.Message.ShouldContain("sealed", Case.Insensitive);

        // Re-assigning the same IRI is a no-op.
        Should.NotThrow(() => entity.AssignIri("https://forge-it.net/foo/1"));
    }

    [Fact]
    public void Accessing_Iri_seals_identity_and_blocks_further_part_changes()
    {
        var foo = new Foo { Slug = "before" };

        // First Iri access triggers lazy materialization and seals identity.
        foo.Iri.ShouldBe("https://forge-it.net/foos/before");
        foo.IsIdentitySealed.ShouldBeTrue();

        // The init accessor calls GuardIdentityMutation(); invoke it via reflection
        // to bypass the compile-time init-only restriction and verify the runtime guard.
        var setter = typeof(Foo).GetProperty(nameof(Foo.Slug))!.SetMethod!;
        Should.Throw<System.Reflection.TargetInvocationException>(
            () => setter.Invoke(foo, ["after"]))
            .InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void MaterializeIdentity_is_idempotent_after_iri_access()
    {
        var foo = new Foo { Slug = "idem" };

        var firstIri = foo.Iri;                // seals via EnsureIdentity
        foo.MaterializeIdentity();             // explicit call on already-sealed entity
        foo.MaterializeIdentity();             // second explicit call — must not throw

        foo.Iri.ShouldBe(firstIri);
        foo.IsIdentitySealed.ShouldBeTrue();
    }

    private sealed class SealableTestEntity : EntityBase
    {
        public void AssignIri(string iri) => Iri = iri;
    }

    // -------------------------------------------------------------- Equality

    [Fact]
    public void Equality_is_defined_by_type_and_iri()
    {
        var a = new Foo { Slug = "x" };
        var b = new Foo { Slug = "x" };
        var c = new Foo { Slug = "y" };

        a.ShouldBe(b);
        a.ShouldNotBe(c);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    // -------------------------------------------------------------- Lazy reference

    [Fact]
    public async Task Awaiting_a_reference_resolves_via_the_ambient_session()
    {
        var bar = new Bar { Name = "child" };

        var foo = new Foo { Slug = "owner" };
        foo.PrimaryBar = EntityRef<Bar>.ForIri(bar.Iri);

        var loader = new InMemoryEntityLoader().Register(bar);
        using var scope = EntitySession.Begin(loader);

        Bar? resolved = await foo.PrimaryBar;

        resolved.ShouldBe(bar);
        foo.PrimaryBar.IsLoaded.ShouldBeTrue();
    }

    [Fact]
    public async Task Awaiting_a_reference_outside_a_session_throws()
    {
        var foo = new Foo { Slug = "lonely" };
        foo.PrimaryBar = EntityRef<Bar>.ForIri("https://forge-it.net/bars/missing");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            _ = await foo.PrimaryBar;
        });
    }

    [Fact]
    public async Task A_reference_that_is_absent_in_the_store_resolves_to_null()
    {
        var foo = new Foo { Slug = "absent-target" };
        foo.PrimaryBar = EntityRef<Bar>.ForIri("https://forge-it.net/bars/does-not-exist");

        using var scope = EntitySession.Begin(new InMemoryEntityLoader());

        (await foo.PrimaryBar).ShouldBeNull();
        foo.PrimaryBar.IsNull.ShouldBeTrue();
    }

    // -------------------------------------------------------------- Owning collection

    [Fact]
    public async Task Owning_collection_iterates_loaded_members()
    {
        var foo = new Foo { Slug = "list-owner" };

        using var scope = EntitySession.Begin(new InMemoryEntityLoader());

        await foo.Bars.AddAsync(new Bar { Name = "a" });
        await foo.Bars.AddAsync(new Bar { Name = "b" });

        var seen = new List<string>();
        await foreach (var b in foo.Bars) seen.Add(b.Name);

        seen.ShouldBe(["a", "b"]);
    }

    // -------------------------------------------------------------- Enumeration

    [Fact]
    public void Enumeration_entities_have_stable_named_iris()
    {
        Color.Red.Iri.ShouldBe("https://forge-it.net/colors/red");
        Color.Green.Iri.ShouldBe("https://forge-it.net/colors/green");
        Color.Blue.Iri.ShouldBe("https://forge-it.net/colors/blue");

        Color.All.ShouldContain(Color.Red);
        Color.Red.ShouldBeSameAs(Color.Red);
    }

    // -------------------------------------------------------------- Inverse sync

    [Fact]
    public void Setting_owning_single_ref_populates_inverse_on_target()
    {
        var foo = new Foo { Slug = "owner" };
        var bar = new Bar { Name = "child" };

        foo.PrimaryBar = EntityRef<Bar>.Loaded(bar);

        bar.Owner.ShouldNotBeNull();
        bar.Owner!.HasValue.ShouldBeTrue();
        bar.Owner.ValueOrThrow.ShouldBe(foo);
    }

    [Fact]
    public void Clearing_owning_single_ref_clears_inverse_on_target()
    {
        var foo = new Foo { Slug = "owner-clear" };
        var bar = new Bar { Name = "child" };

        foo.PrimaryBar = EntityRef<Bar>.Loaded(bar);
        foo.PrimaryBar = null;

        bar.Owner.ShouldBeNull();
    }

    [Fact]
    public async Task Adding_to_owning_collection_populates_inverse_on_target()
    {
        var foo = new Foo { Slug = "list-owner-sync" };

        using var scope = EntitySession.Begin(new InMemoryEntityLoader());
        var bar = new Bar { Name = "child" };

        await foo.Bars.AddAsync(bar);

        _ = bar.Container.ShouldNotBeNull();
        bar.Container!.ValueOrThrow.ShouldBe(foo);
    }

    [Fact]
    public async Task Removing_from_owning_collection_clears_inverse_on_target()
    {
        var foo = new Foo { Slug = "list-owner-clear-sync" };

        using var scope = EntitySession.Begin(new InMemoryEntityLoader());
        var bar = new Bar { Name = "child" };

        await foo.Bars.AddAsync(bar);
        await foo.Bars.RemoveAsync(bar);

        bar.Container.ShouldBeNull();
    }

    // -------------------------------------------------------------- Hydration

    [Fact]
    public void Hydrating_a_uuid_entity_preserves_the_persisted_guid()
    {
        var persisted = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var bar = TestHydration.HydrateBarWithUuid(persisted);

        bar.Iri.ShouldBe($"https://forge-it.net/bars/{persisted:D}");
    }

    // -------------------------------------------------------------- UuidV5

    [Fact]
    public void UuidV5_identity_is_deterministic_for_same_inputs()
    {
        var a = new Widget { Code = "ABC-123" };
        var b = new Widget { Code = "ABC-123" };

        a.Iri.ShouldBe(b.Iri);
        a.Iri.ShouldStartWith("https://forge-it.net/widgets/");
    }

    [Fact]
    public void UuidV5_identity_differs_for_different_inputs()
    {
        var a = new Widget { Code = "ABC-123" };
        var b = new Widget { Code = "XYZ-999" };

        a.Iri.ShouldNotBe(b.Iri);
    }

    [Fact]
    public void UuidV5_produces_an_RFC4122_v5_guid()
    {
        var widget = new Widget { Code = "rfc-check" };

        var suffix = widget.Iri["https://forge-it.net/widgets/".Length..];
        var g = Guid.Parse(suffix);
        // GUID byte 7 high nibble is the version on RFC 4122 wire order;
        // .NET Guid.ToByteArray() uses little-endian for the first three fields,
        // so the version nibble lives in byte index 7.
        var bytes = g.ToByteArray();
        ((bytes[7] & 0xF0) >> 4).ShouldBe(5);
    }

    // -------------------------------------------------------------- m:n inverse-collection sync

    [Fact]
    public async Task Adding_to_owning_collection_populates_inverse_collection_on_target()
    {
        using var scope = EntitySession.Begin(new InMemoryEntityLoader());

        var author = new Author { Name = "Alice" };
        var tag = new Tag { Label = "csharp" };

        await author.Tags.AddAsync(tag);

        // The inverse collection on Tag should now contain the author.
        var seen = new List<Author>();
        await foreach (var a in tag.Authors) seen.Add(a);
        seen.ShouldContain(author);
    }

    [Fact]
    public async Task Removing_from_owning_collection_removes_from_inverse_collection_on_target()
    {
        using var scope = EntitySession.Begin(new InMemoryEntityLoader());

        var author = new Author { Name = "Bob" };
        var tag = new Tag { Label = "dotnet" };

        await author.Tags.AddAsync(tag);
        await author.Tags.RemoveAsync(tag);

        (await tag.Authors.ContainsAsync(author.Iri)).ShouldBeFalse();
    }

    [Fact]
    public async Task ManyToMany_multiple_authors_per_tag_are_tracked_independently()
    {
        using var scope = EntitySession.Begin(new InMemoryEntityLoader());

        var alice = new Author { Name = "Alice" };
        var bob = new Author { Name = "Bob" };
        var tag = new Tag { Label = "oss" };

        await alice.Tags.AddAsync(tag);
        await bob.Tags.AddAsync(tag);

        var seen = new List<Author>();
        await foreach (var a in tag.Authors) seen.Add(a);
        seen.Count.ShouldBe(2);
        seen.ShouldContain(alice);
        seen.ShouldContain(bob);
    }

    // -------------------------------------------------------------- Deferred (lazy) collections

    [Fact]
    public async Task Deferred_collection_loads_iris_and_entities_from_store_on_first_enumeration()
    {
        var tag = new Tag { Label = "oss" };
        var author = new Author { Name = "Charlie" };

        var loader = new InMemoryEntityLoader()
            .Register(tag)
            .RegisterCollection(author.Iri, "hasTag", tag.Iri);

        using var scope = EntitySession.Begin(loader);

        author.Tags.IsResolved.ShouldBeFalse();

        var seen = new List<Tag>();
        await foreach (var t in author.Tags) seen.Add(t);

        seen.Count.ShouldBe(1);
        seen[0].Label.ShouldBe("oss");
        author.Tags.IsResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureLoadedAsync_is_idempotent_and_does_not_reload()
    {
        var tag = new Tag { Label = "net" };
        var author = new Author { Name = "Dana" };

        var loader = new InMemoryEntityLoader()
            .Register(tag)
            .RegisterCollection(author.Iri, "hasTag", tag.Iri);

        using var scope = EntitySession.Begin(loader);

        await author.Tags.EnsureLoadedAsync();
        author.Tags.IsResolved.ShouldBeTrue();
        author.Tags.Iris.Count.ShouldBe(1);

        // Remove the registration so a second load would return nothing
        loader.RegisterCollection(author.Iri, "hasTag" /* override with empty */);

        // Calling again must be a no-op (already resolved)
        await author.Tags.EnsureLoadedAsync();
        author.Tags.Iris.Count.ShouldBe(1); // still 1, not reset
    }

    // -------------------------------------------------------------- EntityOptions.Current / Use

    [Fact]
    public void EntityOptions_Current_reflects_static_BaseIri()
    {
        EntityOptions.Current.BaseIri.ShouldBe("https://forge-it.net");
    }

    [Fact]
    public void EntityOptions_Use_overrides_Current_for_the_scope()
    {
        var scoped = new EntityOptionsInstance { BaseIri = "https://tenant.example" };

        using (EntityOptions.Use(scoped))
        {
            EntityOptions.Current.BaseIri.ShouldBe("https://tenant.example");
        }

        // Restored after disposal.
        EntityOptions.Current.BaseIri.ShouldBe("https://forge-it.net");
    }

    [Fact]
    public void EntityOptions_Use_scope_affects_entity_Iri_materialization()
    {
        var scoped = new EntityOptionsInstance { BaseIri = "https://custom.example" };

        using (EntityOptions.Use(scoped))
        {
            var foo = new Foo { Slug = "scoped-test" };
            foo.Iri.ShouldBe("https://custom.example/foos/scoped-test");
        }
    }

    // -------------------------------------------------------------- Iri factory

    [Fact]
    public void Iri_FromBaseUrl_combines_base_and_path()
    {
        Iri.FromBaseUrl("/entity/myentity").ShouldBe("https://forge-it.net/entity/myentity");
    }

    [Fact]
    public void Iri_FromBaseUrl_normalizes_leading_slash()
    {
        Iri.FromBaseUrl("entity/myentity").ShouldBe("https://forge-it.net/entity/myentity");
    }

    [Fact]
    public void Iri_FromEntity_uses_entity_path_attribute()
    {
        Iri.FromEntity<Bar>("myentity").ShouldBe("https://forge-it.net/bars/myentity");
    }

    [Fact]
    public void Iri_FromEntity_respects_ambient_options()
    {
        var scoped = new EntityOptionsInstance { BaseIri = "https://tenant.example" };

        using (EntityOptions.Use(scoped))
        {
            Iri.FromEntity<Bar>("thing").ShouldBe("https://tenant.example/bars/thing");
        }
    }
}

internal static class TestHydration
{
    // Exposes the internal hydration constructor through the test assembly.
    public static Bar HydrateBarWithUuid(Guid uuid) =>
        (Bar)Activator.CreateInstance(typeof(Bar),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, args: [uuid], culture: null)!;
}
