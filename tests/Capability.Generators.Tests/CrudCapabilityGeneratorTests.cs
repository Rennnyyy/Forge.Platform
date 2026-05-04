using Microsoft.CodeAnalysis;
using Shouldly;

namespace Forge.Capability.Generators.Tests;

/// <summary>
/// Behavioural snapshot tests for <see cref="CrudCapabilityGenerator"/>.
/// Each test drives the generator over minimal source, then asserts on the emitted
/// source content — following the pattern established by <c>Operations.Tests</c>.
/// </summary>
public sealed class CrudCapabilityGeneratorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string FileName, string Source) CapsFile(
        CrudCapabilityGeneratorRunner.RunResult result, string typeNameFragment)
        => result.EmittedFiles.Single(f =>
            f.FileName.Contains(typeNameFragment) && f.FileName.EndsWith(".g.caps.cs"));

    private static string MinimalEntity(string extra = "") => """
        using Forge.Entity;
        using Forge.Capability;
        namespace Demo;

        [Entity(Path = "widgets")]
        [Identity(IdentityGenerator.Random)]
        [CrudCapabilities]
        public partial class Widget
        {
            [Predicate("label")]
            public string Label { get; set; } = "";
        """ + extra + "\n}";

    // ════════════════════════════════════════════════════════════════════════
    // 1. [CrudCapabilities] on an entity produces a .g.caps.cs file
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Entity_with_CrudCapabilities_produces_caps_file()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());

        result.Diagnostics.ShouldBeEmpty();
        result.EmittedFiles.ShouldContain(f => f.FileName.EndsWith(".g.caps.cs"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Entity without [CrudCapabilities] produces no .g.caps.cs file
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Entity_without_CrudCapabilities_produces_no_caps_file()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "items")]
            [Identity(IdentityGenerator.Random)]
            public partial class Item { }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);

        result.EmittedFiles.ShouldNotContain(f => f.FileName.EndsWith(".g.caps.cs"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. CrudMethod.All (default) emits all five handler classes
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Default_CrudMethod_All_emits_all_five_handlers()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (_, code) = CapsFile(result, "Widget");

        code.ShouldContain("class CreateWidgetHandler");
        code.ShouldContain("class ReadWidgetHandler");
        code.ShouldContain("class UpdateWidgetHandler");
        code.ShouldContain("class DeleteWidgetHandler");
        code.ShouldContain("class ListWidgetHandler");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. CrudMethod subset emits only requested handlers
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CrudMethod_Create_only_emits_create_handler()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "nodes")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class Node { }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Node");

        code.ShouldContain("class CreateNodeHandler");
        code.ShouldNotContain("class ReadNodeHandler");
        code.ShouldNotContain("class UpdateNodeHandler");
        code.ShouldNotContain("class DeleteNodeHandler");
        code.ShouldNotContain("class ListNodeHandler");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Capability identities use Entity.Path when set
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Capability_identity_uses_entity_path()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (_, code) = CapsFile(result, "Widget");

        code.ShouldContain("CapabilityAttribute(\"widgets.create\")");
        code.ShouldContain("CapabilityAttribute(\"widgets.read\")");
        code.ShouldContain("CapabilityAttribute(\"widgets.update\")");
        code.ShouldContain("CapabilityAttribute(\"widgets.delete\")");
        code.ShouldContain("CapabilityAttribute(\"widgets.list\")");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Capability identities fall back to lowercased type name when Path absent
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Capability_identity_falls_back_to_lowercased_type_name()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class Product { }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Product");

        code.ShouldContain("CapabilityAttribute(\"product.create\")");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. Create command includes identity parts and predicate properties
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Create_command_includes_identity_parts_and_predicate_props()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "artists")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class Artist
            {
                [IdentityPart(0)] public partial string Name { get; init; }
                [Predicate("bio")] public string? Bio { get; set; }
                [Predicate("active")] public bool Active { get; set; }
            }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Artist");

        // Create command must contain identity part and predicate params
        code.ShouldContain("CreateArtistCommand");
        code.ShouldContain("string Name");
        code.ShouldContain("string? Bio");
        code.ShouldContain("bool Active");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 8. Create handler body uses object initializer with all props
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Create_handler_builds_entity_via_object_initializer()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "tags")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class Tag
            {
                [IdentityPart(0)] public partial string Slug { get; init; }
                [Predicate("color")] public string Color { get; set; } = "";
            }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Tag");

        code.ShouldContain("Slug = command.Slug");
        code.ShouldContain("Color = command.Color");
        code.ShouldContain("await entity.CreateAsync(cancellationToken)");
        code.ShouldContain("new CreateTagResponse(entity.Iri)");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 9. Update command contains only settable (non-init-only) data props
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Update_command_excludes_init_only_and_identity_props()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "albums")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            [CrudCapabilities(CrudMethod.Update)]
            public partial class Album
            {
                [IdentityPart(0)] public partial string Slug { get; init; }
                [Predicate("title")] public string Title { get; set; } = "";
                [Predicate("year")] public int Year { get; set; }
            }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Album");

        // Update command: Iri + settable data props (no Slug which is init-only identity part)
        code.ShouldContain("UpdateAlbumCommand(string Iri");
        code.ShouldContain("string Title");
        code.ShouldContain("int Year");
        code.ShouldNotContain("string Slug");  // identity part — init-only, excluded from Update
    }

    // ════════════════════════════════════════════════════════════════════════
    // 10. Update handler loads entity, assigns props, calls UpdateAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Update_handler_loads_entity_updates_props_and_calls_UpdateAsync()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "cats")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Update)]
            public partial class Cat
            {
                [Predicate("name")] public string Name { get; set; } = "";
            }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Cat");

        code.ShouldContain("ReadAsync(command.Iri");
        code.ShouldContain("entity.Name = command.Name");
        code.ShouldContain("await entity.UpdateAsync(cancellationToken)");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 11. Read handler maps all entity props to ReadResponse
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Read_handler_maps_all_props_to_ReadResponse()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "books")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            [CrudCapabilities(CrudMethod.Read)]
            public partial class Book
            {
                [IdentityPart(0)] public partial string Isbn { get; init; }
                [Predicate("title")] public string Title { get; set; } = "";
                [Predicate("pages")] public int Pages { get; set; }
            }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Book");

        code.ShouldContain("ReadBookResponse(string Iri");
        code.ShouldContain("string Isbn");
        code.ShouldContain("string Title");
        code.ShouldContain("int Pages");
        code.ShouldContain("entity.Iri");
        code.ShouldContain("entity.Isbn");
        code.ShouldContain("entity.Title");
        code.ShouldContain("entity.Pages");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 12. Delete handler loads entity and calls DeleteAsync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Delete_handler_loads_entity_and_calls_DeleteAsync()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (_, code) = CapsFile(result, "Widget");

        code.ShouldContain("class DeleteWidgetHandler");
        code.ShouldContain("ReadAsync(command.Iri");
        code.ShouldContain("await entity.DeleteAsync(cancellationToken)");
        code.ShouldContain("new DeleteWidgetResponse()");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 13. List handler streams entity and maps to ReadResponse items
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void List_handler_uses_ListAsync_and_wraps_items_in_ReadResponse()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (_, code) = CapsFile(result, "Widget");

        code.ShouldContain("ListWidgetResponse");
        code.ShouldContain("IReadOnlyList<ReadWidgetResponse>");
        code.ShouldContain("await foreach");
        code.ShouldContain("Widget.ListAsync(cancellationToken)");
        code.ShouldContain("new ReadWidgetResponse(entity.Iri");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 14. List only (without Read) still emits ReadResponse as the item type
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void List_only_still_emits_ReadResponse_as_item_type()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "tags")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.List)]
            public partial class Tag
            {
                [Predicate("name")] public string Name { get; set; } = "";
            }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Tag");

        code.ShouldContain("ReadTagResponse");           // item record emitted
        code.ShouldNotContain("class ReadTagHandler");   // handler NOT emitted
        code.ShouldContain("class ListTagHandler");
        code.ShouldContain("IReadOnlyList<ReadTagResponse>");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 15. Not-found guard is emitted for Read, Update, Delete
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Not_found_guard_is_emitted_for_read_update_delete()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (_, code) = CapsFile(result, "Widget");

        code.ShouldContain("NOT_FOUND");
        code.ShouldContain("Widget '{command.Iri}' not found.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 16. Global-namespace entity emits without namespace declaration
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Global_namespace_entity_emits_without_namespace_declaration()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;

            [Entity(Path = "roots")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class RootEntity { }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);

        result.EmittedFiles.ShouldContain(f => f.FileName == "RootEntity.g.caps.cs");
        var (_, code) = CapsFile(result, "RootEntity");
        code.ShouldNotContain("namespace ");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 17. Namespace is emitted for namespaced entity
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Namespaced_entity_emits_correct_namespace_declaration()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (fileName, code) = CapsFile(result, "Widget");

        fileName.ShouldBe("Demo.Widget.g.caps.cs");
        code.ShouldContain("namespace Demo;");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 18. Multiple entities each get their own caps file
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_entities_each_get_own_caps_file()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "cats")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class Cat { }

            [Entity(Path = "dogs")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Create)]
            public partial class Dog { }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);

        result.EmittedFiles.ShouldContain(f => f.FileName == "Demo.Cat.g.caps.cs");
        result.EmittedFiles.ShouldContain(f => f.FileName == "Demo.Dog.g.caps.cs");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 19. No generator diagnostics for a well-formed entity
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void No_diagnostics_for_well_formed_entity()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());

        result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 20. [Owning] and [Inverse] properties are excluded from commands/responses
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Owning_and_inverse_refs_are_excluded_from_generated_types()
    {
        var src = """
            using Forge.Entity;
            using Forge.Capability;
            namespace Demo;

            [Entity(Path = "artists")]
            [Identity(IdentityGenerator.Random)]
            [CrudCapabilities(CrudMethod.Create | CrudMethod.Read)]
            public partial class Artist
            {
                [Predicate("name")] public string Name { get; set; } = "";
                [Owning("demo:hasAlbum")] public EntityRefCollection<Album> Albums { get; } = new();
            }

            [Entity(Path = "albums")]
            [Identity(IdentityGenerator.Random)]
            public partial class Album { }
            """;

        var result = CrudCapabilityGeneratorRunner.Run(src);
        var (_, code) = CapsFile(result, "Artist");

        code.ShouldContain("string Name");   // predicate prop included
        code.ShouldNotContain("Albums");      // owning ref excluded
        code.ShouldNotContain("Album");
    }

    // ════════════════════════════════════════════════════════════════════════
    // N. Every generated handler carries [CrudCapabilityHandlerAttribute]
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Every_generated_handler_carries_CrudCapabilityHandler_attribute()
    {
        var result = CrudCapabilityGeneratorRunner.Run(MinimalEntity());
        var (_, code) = CapsFile(result, "Widget");

        // Each handler class must be preceded by the [CrudCapabilityHandlerAttribute].
        code.ShouldContain("CrudCapabilityHandlerAttribute]");
    }
}
