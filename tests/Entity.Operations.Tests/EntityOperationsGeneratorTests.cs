using Microsoft.CodeAnalysis;
using Shouldly;

namespace Forge.Entity.Operations.Tests;

/// <summary>
/// Verifies that <see cref="EntityOperationsGenerator"/> emits correct CRUD method stubs
/// for various entity shapes (path, uuid, sealed, global namespace).
/// </summary>
public sealed class EntityOperationsGeneratorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string FileName, string Source) OpsFile(
        OperationsGeneratorRunner.RunResult result, string typeNameFragment)
        => result.EmittedFiles.Single(f => f.FileName.Contains(typeNameFragment) && f.FileName.EndsWith(".g.ops.cs"));

    // ════════════════════════════════════════════════════════════════════════
    // 1. Path-strategy entity gets all five methods
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Path_entity_gets_all_five_crud_methods()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "items")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            public partial class Item
            {
                [IdentityPart(0)] public partial string Slug { get; init; }
            }
            """;

        var result = OperationsGeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var (_, code) = OpsFile(result, "Item");

        code.ShouldContain("partial class Item");
        code.ShouldContain("public global::System.Threading.Tasks.ValueTask CreateAsync(");
        code.ShouldContain("public global::System.Threading.Tasks.ValueTask UpdateAsync(");
        code.ShouldContain("public global::System.Threading.Tasks.ValueTask DeleteAsync(");
        code.ShouldContain("public static global::System.Threading.Tasks.ValueTask<Item?> ReadAsync(");
        code.ShouldContain("public static global::System.Collections.Generic.IAsyncEnumerable<Item> ListAsync(");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. All methods delegate to EntityOperations static helpers
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Methods_delegate_to_EntityOperations_static_helpers()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "things")]
            [Identity(IdentityGenerator.Random)]
            public partial class Thing { }
            """;

        var result = OperationsGeneratorRunner.Run(src);
        var (_, code) = OpsFile(result, "Thing");

        code.ShouldContain("global::Forge.Entity.Operations.EntityOperations.CreateAsync(this");
        code.ShouldContain("global::Forge.Entity.Operations.EntityOperations.UpdateAsync(this");
        code.ShouldContain("global::Forge.Entity.Operations.EntityOperations.DeleteAsync(Iri");
        code.ShouldContain("global::Forge.Entity.Operations.EntityOperations.ReadAsync<Thing>(iri");
        code.ShouldContain("global::Forge.Entity.Operations.EntityOperations.ListAsync<Thing>(");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. Sealed entities emit the sealed modifier
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Sealed_entity_emits_sealed_partial()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "nodes")]
            [Identity(IdentityGenerator.Random)]
            public sealed partial class Node { }
            """;

        var result = OperationsGeneratorRunner.Run(src);
        var (_, code) = OpsFile(result, "Node");

        code.ShouldContain("sealed partial class Node");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Global-namespace entity (no namespace declaration)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Global_namespace_entity_emits_without_namespace_declaration()
    {
        var src = """
            using Forge.Entity;

            [Entity(Path = "roots")]
            [Identity(IdentityGenerator.Random)]
            public partial class RootEntity { }
            """;

        var result = OperationsGeneratorRunner.Run(src);
        var (fileName, code) = OpsFile(result, "RootEntity");

        fileName.ShouldBe("RootEntity.g.ops.cs");
        code.ShouldNotContain("namespace ");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5. Hint file is distinct from the entity generator hint file
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Hint_file_suffix_is_g_ops_cs_distinct_from_entity_generator()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "widgets")]
            [Identity(IdentityGenerator.Random)]
            public partial class Widget { }
            """;

        var result = OperationsGeneratorRunner.Run(src);

        result.EmittedFiles.ShouldContain(f => f.FileName == "Demo.Widget.g.cs");
        result.EmittedFiles.ShouldContain(f => f.FileName == "Demo.Widget.g.ops.cs");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 6. Multiple entities in the same compilation each get their own ops file
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_entities_each_get_own_ops_file()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "cats")]
            [Identity(IdentityGenerator.Random)]
            public partial class Cat { }

            [Entity(Path = "dogs")]
            [Identity(IdentityGenerator.Random)]
            public partial class Dog { }
            """;

        var result = OperationsGeneratorRunner.Run(src);

        result.EmittedFiles.ShouldContain(f => f.FileName == "Demo.Cat.g.ops.cs");
        result.EmittedFiles.ShouldContain(f => f.FileName == "Demo.Dog.g.ops.cs");
        OpsFile(result, "Cat").Source.ShouldContain("ReadAsync<Cat>");
        OpsFile(result, "Dog").Source.ShouldContain("ReadAsync<Dog>");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 7. No diagnostics emitted for a well-formed entity
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void No_diagnostics_for_well_formed_entity()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "samples")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            public partial class Sample
            {
                [IdentityPart(0)] public partial string Name { get; init; }
            }
            """;

        var result = OperationsGeneratorRunner.Run(src);

        result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }
}
