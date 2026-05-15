using Forge.Entity;
using Shouldly;

namespace Forge.Structure.Tests;

/// <summary>
/// Behavioral tests for the <see cref="Usage"/> entity: IRI generation, identity
/// sealing, and <see cref="IEntity"/> contract.
/// </summary>
public sealed class UsageEntityTests
{
    [Fact]
    public void Usage_Iri_is_a_non_empty_string()
    {
        var usage = new Usage
        {
            ParentStructureIri = "https://forge-it.net/structures/parent",
            ChildStructureIri = "https://forge-it.net/structures/child"
        };

        usage.Iri.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Usage_Iri_is_sealed_after_first_access()
    {
        var usage = new Usage
        {
            ParentStructureIri = "https://forge-it.net/structures/parent",
            ChildStructureIri = "https://forge-it.net/structures/child"
        };
        _ = usage.Iri;

        usage.IsIdentitySealed.ShouldBeTrue();
    }

    [Fact]
    public void Two_Usage_instances_have_different_Iris()
    {
        // Random identity: two independently created instances must not collide
        var a = new Usage
        {
            ParentStructureIri = "https://forge-it.net/structures/P",
            ChildStructureIri = "https://forge-it.net/structures/C"
        };
        var b = new Usage
        {
            ParentStructureIri = "https://forge-it.net/structures/P",
            ChildStructureIri = "https://forge-it.net/structures/C"
        };

        a.Iri.ShouldNotBe(b.Iri);
    }

    [Fact]
    public void Usage_implements_IEntity()
    {
        var usage = new Usage();

        ((IEntity)usage).ShouldNotBeNull();
    }

    [Fact]
    public void Usage_Iri_contains_usages_path_segment()
    {
        var usage = new Usage();

        usage.Iri.ShouldContain("usages");
    }

    [Fact]
    public void Usage_Conditions_defaults_to_ConditionSet_Empty()
    {
        var usage = new Usage();

        usage.Conditions.ShouldBeSameAs(ConditionSet.Empty);
    }

    [Fact]
    public void Usage_ParentStructureIri_and_ChildStructureIri_are_readable_after_set()
    {
        var usage = new Usage
        {
            ParentStructureIri = "https://forge-it.net/structures/A",
            ChildStructureIri = "https://forge-it.net/structures/B"
        };

        usage.ParentStructureIri.ShouldBe("https://forge-it.net/structures/A");
        usage.ChildStructureIri.ShouldBe("https://forge-it.net/structures/B");
    }
}
