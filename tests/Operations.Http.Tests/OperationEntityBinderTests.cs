using System.Text.Json.Nodes;
using Forge.Entity;
using Shouldly;

namespace Forge.Operations.Http.Tests;

/// <summary>
/// Unit tests for <see cref="OperationEntityBinder"/> covering JSON-to-entity
/// materialisation for both identity strategies.
/// </summary>
public sealed class OperationEntityBinderTests
{
    // ── TestWidget (Random / UuidV4) — Create ─────────────────────────────────

    [Fact]
    public void CreateFromJson_RandomEntity_SetsPredicateProperties()
    {
        var body = new JsonObject
        {
            ["label"] = "Sprocket",
            ["value"] = 42,
        };

        var widget = OperationEntityBinder.CreateFromJson<TestWidget>(body);

        widget.Label.ShouldBe("Sprocket");
        widget.Value.ShouldBe(42);
    }

    [Fact]
    public void CreateFromJson_RandomEntity_IriIsNonEmpty()
    {
        var body = new JsonObject { ["label"] = "Cog" };

        var widget = OperationEntityBinder.CreateFromJson<TestWidget>(body);

        widget.Iri.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreateFromJson_RandomEntity_EachCallYieldsDistinctIri()
    {
        var body = new JsonObject { ["label"] = "X" };

        var w1 = OperationEntityBinder.CreateFromJson<TestWidget>(body);
        var w2 = OperationEntityBinder.CreateFromJson<TestWidget>(body);

        w1.Iri.ShouldNotBe(w2.Iri);
    }

    [Fact]
    public void CreateFromJson_RandomEntity_IgnoresUnknownKeys()
    {
        var body = new JsonObject
        {
            ["label"] = "Known",
            ["unknown"] = "ignored",
        };

        var widget = OperationEntityBinder.CreateFromJson<TestWidget>(body);

        widget.Label.ShouldBe("Known");
    }

    // ── TestWidget (Random / UuidV4) — Update ─────────────────────────────────

    [Fact]
    public void UpdateFromJson_RandomEntity_UsesProvidedIri()
    {
        var uuid = Guid.NewGuid();
        var iri = $"https://forge-it.net/test-widgets/{uuid}";
        var body = new JsonObject { ["label"] = "Updated", ["value"] = 99 };

        var (entity, error) = OperationEntityBinder.UpdateFromJson<TestWidget>(iri, body);

        error.ShouldBeNull();
        entity.ShouldNotBeNull();
        entity!.Iri.ShouldBe(iri);
        entity.Label.ShouldBe("Updated");
        entity.Value.ShouldBe(99);
    }

    [Fact]
    public void UpdateFromJson_RandomEntity_InvalidIri_ReturnsError()
    {
        var body = new JsonObject { ["label"] = "x" };

        var (entity, error) = OperationEntityBinder.UpdateFromJson<TestWidget>(
            "https://forge-it.net/test-widgets/not-a-guid", body);

        entity.ShouldBeNull();
        error.ShouldNotBeNull();
        error!.Code.ShouldBe("INVALID_IRI");
    }

    // ── TestTag (PropertyBasedEncoded / UuidV5) — Create ──────────────────────

    [Fact]
    public void CreateFromJson_PropertyBasedEntity_SetsIdentityPartsAndPredicates()
    {
        var body = new JsonObject
        {
            ["namespace"] = "forge",
            ["name"] = "core",
            ["description"] = "Core tag",
        };

        var tag = OperationEntityBinder.CreateFromJson<TestTag>(body);

        tag.Namespace.ShouldBe("forge");
        tag.Name.ShouldBe("core");
        tag.Description.ShouldBe("Core tag");
        tag.Iri.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreateFromJson_PropertyBasedEntity_DeterministicIri()
    {
        var body = new JsonObject { ["namespace"] = "ns", ["name"] = "tag" };

        var t1 = OperationEntityBinder.CreateFromJson<TestTag>(body);
        var t2 = OperationEntityBinder.CreateFromJson<TestTag>(body);

        t1.Iri.ShouldBe(t2.Iri);
    }

    // ── TestTag (PropertyBasedEncoded) — Update ────────────────────────────────

    [Fact]
    public void UpdateFromJson_PropertyBasedEntity_MatchingIri_Succeeds()
    {
        // Compute the expected IRI using a Create first.
        var body = new JsonObject
        {
            ["namespace"] = "forge",
            ["name"] = "v2",
        };
        var created = OperationEntityBinder.CreateFromJson<TestTag>(body);

        // Update with the same identity parts and matching IRI → should succeed.
        var updateBody = new JsonObject
        {
            ["namespace"] = "forge",
            ["name"] = "v2",
            ["description"] = "Updated description",
        };

        var (entity, error) = OperationEntityBinder.UpdateFromJson<TestTag>(created.Iri, updateBody);

        error.ShouldBeNull();
        entity.ShouldNotBeNull();
        entity!.Iri.ShouldBe(created.Iri);
        entity.Description.ShouldBe("Updated description");
    }

    [Fact]
    public void UpdateFromJson_PropertyBasedEntity_MismatchedIri_ReturnsError()
    {
        // Build the IRI for ns="a", name="1"
        var body1 = new JsonObject { ["namespace"] = "a", ["name"] = "1" };
        var tag1 = OperationEntityBinder.CreateFromJson<TestTag>(body1);

        // Now try to update with body for ns="b", name="2" but IRI for ns="a", name="1"
        var wrongBody = new JsonObject { ["namespace"] = "b", ["name"] = "2" };
        var (entity, error) = OperationEntityBinder.UpdateFromJson<TestTag>(tag1.Iri, wrongBody);

        entity.ShouldBeNull();
        error.ShouldNotBeNull();
        error!.Code.ShouldBe("IRI_MISMATCH");
    }

    // ── Plan caching ───────────────────────────────────────────────────────────

    [Fact]
    public void GetPlan_ReturnsSamePlanOnSecondCall()
    {
        var plan1 = OperationEntityBinder.GetPlan(typeof(TestWidget));
        var plan2 = OperationEntityBinder.GetPlan(typeof(TestWidget));

        plan1.ShouldBeSameAs(plan2);
    }

    [Fact]
    public void GetPlan_RandomEntity_HasNullGuidCtorOnlyWhenNoInternalCtor()
    {
        // TestTag is PropertyBasedEncoded → GuidCtor should be null
        var plan = OperationEntityBinder.GetPlan(typeof(TestTag));

        plan.GuidCtor.ShouldBeNull();
    }
}
