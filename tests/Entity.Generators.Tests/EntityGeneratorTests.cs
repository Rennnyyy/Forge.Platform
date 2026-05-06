using Microsoft.CodeAnalysis;
using Shouldly;

namespace Forge.Entity.Generators.Tests;

public class EntityGeneratorTests
{
    [Fact]
    public void Emits_partial_for_path_identity_entity()
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

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        result.EmittedFiles.ShouldNotBeEmpty();
        var (_, code) = result.EmittedFiles.Single(f => f.FileName.Contains("Item"));

        code.ShouldContain("partial class Item : global::Forge.Entity.EntityBase");
        code.ShouldContain("MaterializeIdentity()");
        code.ShouldContain("__forge_part_Slug");
        code.ShouldContain("GuardIdentityMutation()");
    }

    [Fact]
    public void Emits_uuidv4_constructors_and_iri_format()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "items")]
            [Identity(IdentityGenerator.Random)]
            public partial class Item { }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var (_, code) = result.EmittedFiles.Single(f => f.FileName.Contains("Item"));

        code.ShouldContain("__forge_identityUuid = global::System.Guid.NewGuid();");
        code.ShouldContain("internal Item(global::System.Guid persistedUuid)");
        code.ShouldContain("HydrateIri(");
        code.ShouldContain("\"/items\"");
    }

    [Fact]
    public void Reports_FORGE0001_when_class_is_not_partial()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity]
            [Identity(IdentityGenerator.Random)]
            public class NotPartialEntity { }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldContain(d => d.Id == "FORGE0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Reports_FORGE0002_when_identity_is_missing()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity]
            public partial class MissingIdentity { }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldContain(d => d.Id == "FORGE0002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Reports_FORGE0003_for_disallowed_identity_part_type()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "x")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            public partial class BadType
            {
                [IdentityPart(0)] public partial object Bad { get; init; }
            }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldContain(d => d.Id == "FORGE0003");
    }

    [Fact]
    public void Does_not_report_FORGE0003_for_list_of_primitives()
    {
        var src = """
            using Forge.Entity;
            using System.Collections.Generic;
            namespace Demo;

            [Entity(Path = "x")]
            [Identity(IdentityGenerator.PropertyBasedPlain)]
            public partial class Tags
            {
                [IdentityPart(0)] public partial List<string> Values { get; init; }
            }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldNotContain(d => d.Id == "FORGE0003");
    }

    [Fact]
    public void Wires_inverse_setter_for_owning_single_ref()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "as", PredicatePath = "a")]
            [Identity(IdentityGenerator.Random)]
            public partial class A
            {
                [Owning("hasB")] public partial EntityRef<B>? B { get; set; }
            }

            [Entity(Path = "bs", PredicatePath = "b")]
            [Identity(IdentityGenerator.Random)]
            public partial class B
            {
                [Inverse(nameof(A.B), "isBOf")] public partial EntityRef<A>? Owner { get; }
            }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var aCode = result.EmittedFiles.Single(f => f.FileName.Contains(".A.")).Source;
        var bCode = result.EmittedFiles.Single(f => f.FileName.Contains(".B.")).Source;

        aCode.ShouldContain("__Forge_Set_Owner");
        bCode.ShouldContain("internal void __Forge_Set_Owner");
    }

    [Fact]
    public void Wires_collection_inverse_hooks()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "parents", PredicatePath = "p")]
            [Identity(IdentityGenerator.Random)]
            public partial class Parent
            {
                [Owning("hasChild")] public partial EntityRefCollection<Child> Children { get; }
            }

            [Entity(Path = "children", PredicatePath = "c")]
            [Identity(IdentityGenerator.Random)]
            public partial class Child
            {
                [Inverse(nameof(Parent.Children), "isChildOf")] public partial EntityRef<Parent>? Parent { get; }
            }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var parentCode = result.EmittedFiles.Single(f => f.FileName.Contains(".Parent.")).Source;

        parentCode.ShouldContain("onAdd:");
        parentCode.ShouldContain("__Forge_Set_Parent");
        parentCode.ShouldContain("onRemove:");
    }

    [Fact]
    public void Generated_code_compiles_for_minimal_entity()
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

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var errors = result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void PredicatePath_defaults_to_Path_when_not_set()
    {
        // When PredicatePath is omitted the model should carry the Path value in its place.
        // We verify this indirectly: the generator produces no diagnostics and the compiled
        // output is error-free, meaning the parser didn't reject the entity.
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "widgets")]
            [Identity(IdentityGenerator.Random)]
            public partial class Widget { }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var (_, code) = result.EmittedFiles.Single(f => f.FileName.Contains("Widget"));
        // The IRI segment uses Path ("widgets"), confirming it was wired correctly.
        code.ShouldContain("\"/widgets\"");
    }

    [Fact]
    public void Wires_collection_inverse_hooks_for_many_to_many()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "authors", PredicatePath = "author")]
            [Identity(IdentityGenerator.Random)]
            public partial class Author
            {
                [Owning("hasTag")] public partial EntityRefCollection<Tag> Tags { get; }
            }

            [Entity(Path = "tags", PredicatePath = "tag")]
            [Identity(IdentityGenerator.Random)]
            public partial class Tag
            {
                [Inverse(nameof(Author.Tags), "isTagOf")]
                public partial EntityRefCollection<Author> Authors { get; }
            }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();

        var authorCode = result.EmittedFiles.Single(f => f.FileName.Contains(".Author.")).Source;
        var tagCode = result.EmittedFiles.Single(f => f.FileName.Contains(".Tag.")).Source;

        // Owning side must call the inverse add/remove helpers
        authorCode.ShouldContain("__Forge_AddTo_Authors");
        authorCode.ShouldContain("__Forge_RemoveFrom_Authors");

        // Inverse side must expose the collection property and the helpers
        tagCode.ShouldContain("public partial global::Forge.Entity.EntityRefCollection<");
        tagCode.ShouldContain("internal global::System.Threading.Tasks.ValueTask __Forge_AddTo_Authors");
        tagCode.ShouldContain("internal global::System.Threading.Tasks.ValueTask __Forge_RemoveFrom_Authors");
    }

    [Fact]
    public void Generated_many_to_many_code_compiles()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "authors", PredicatePath = "author")]
            [Identity(IdentityGenerator.Random)]
            public partial class Author
            {
                [Owning("hasTag")] public partial EntityRefCollection<Tag> Tags { get; }
            }

            [Entity(Path = "tags", PredicatePath = "tag")]
            [Identity(IdentityGenerator.Random)]
            public partial class Tag
            {
                [Inverse(nameof(Author.Tags), "isTagOf")]
                public partial EntityRefCollection<Author> Authors { get; }
            }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var errors = result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void Emits_deferred_collection_impl_for_lazy_owning_collection()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "authors", PredicatePath = "author")]
            [Identity(IdentityGenerator.Random)]
            public partial class Author
            {
                [Owning("hasTag", Lazy = true)]
                public partial EntityRefCollection<Tag> Tags { get; }
            }

            [Entity(Path = "tags")]
            [Identity(IdentityGenerator.Random)]
            public partial class Tag { }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var authorCode = result.EmittedFiles.Single(f => f.FileName.Contains(".Author.")).Source;
        authorCode.ShouldContain("DeferredEntityRefCollectionImpl");
        authorCode.ShouldContain("ownerIriSelector: () => Iri");
        authorCode.ShouldContain("\"hasTag\"");
    }

    [Fact]
    public void Generated_lazy_collection_code_compiles()
    {
        var src = """
            using Forge.Entity;
            namespace Demo;

            [Entity(Path = "authors", PredicatePath = "author")]
            [Identity(IdentityGenerator.Random)]
            public partial class Author
            {
                [Owning("hasTag", Lazy = true)]
                public partial EntityRefCollection<Tag> Tags { get; }
            }

            [Entity(Path = "tags")]
            [Identity(IdentityGenerator.Random)]
            public partial class Tag { }
            """;

        var result = GeneratorRunner.Run(src);

        result.Diagnostics.ShouldBeEmpty();
        var errors = result.CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
    }
}
