using Forge.Entity;
using Shouldly;

namespace Forge.Branch.Tests;

/// <summary>
/// Tests for the <see cref="Branch"/> entity type and its source-generated IRI.
/// </summary>
public sealed class BranchEntityTests
{
    [Fact]
    public void Branch_Iri_is_computed_from_Name()
    {
        var branch = new Branch { Name = "main" };

        // IRI = {EntityOptions.BaseIri}/branches/main
        branch.Iri.ShouldBe($"{EntityOptions.Current.BaseIri.TrimEnd('/')}/branches/main");
    }

    [Fact]
    public void Branch_Iri_is_sealed_after_first_access()
    {
        var branch = new Branch { Name = "feature-x" };
        _ = branch.Iri;

        branch.IsIdentitySealed.ShouldBeTrue();
    }

    [Fact]
    public void Branch_Iri_matches_expected_pattern_for_feature_branch()
    {
        var branch = new Branch { Name = "feature-abc" };
        var base_ = EntityOptions.Current.BaseIri.TrimEnd('/');

        branch.Iri.ShouldBe($"{base_}/branches/feature-abc");
    }

    [Fact]
    public void Branch_Description_defaults_to_null()
    {
        var branch = new Branch { Name = "main" };

        branch.Description.ShouldBeNull();
    }

    [Fact]
    public void Branch_implements_IEntity()
    {
        var branch = new Branch { Name = "main" };

        ((IEntity)branch).ShouldNotBeNull();
    }

    [Fact]
    public void Two_branches_with_same_Name_have_same_Iri()
    {
        var a = new Branch { Name = "main" };
        var b = new Branch { Name = "main" };

        a.Iri.ShouldBe(b.Iri);
    }

    [Fact]
    public void Two_branches_with_different_Names_have_different_Iris()
    {
        var a = new Branch { Name = "main" };
        var b = new Branch { Name = "dev" };

        a.Iri.ShouldNotBe(b.Iri);
    }
}
