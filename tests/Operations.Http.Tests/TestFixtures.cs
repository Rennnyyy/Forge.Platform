using Forge.Entity;

namespace Forge.Operations.Http.Tests;

// ──────────────────────────────────────────────────────────────────────────────────
// Test entity: Random (UuidV4) identity — no identity parts.
// Demonstrates the POST/GET/PUT/DELETE Operation endpoint wiring for entities
// whose IRI is generated on creation.
// ──────────────────────────────────────────────────────────────────────────────────

[Entity(Path = "test-widgets")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class TestWidget
{
    [Predicate("label")]
    public string Label { get; set; } = "";

    [Predicate("value")]
    public int Value { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────────
// Test entity: PropertyBasedEncoded (UuidV5) identity — two identity parts.
// Demonstrates IRI verification on Update: the body's identity parts must compute
// to the same IRI as the ?iri= parameter.
// ──────────────────────────────────────────────────────────────────────────────────

[Entity(Path = "test-tags")]
[Identity(IdentityGenerator.PropertyBasedEncoded)]
[OperationEndpoints]
public partial class TestTag
{
    [IdentityPart(0)]
    [Predicate("namespace")]
    public partial string Namespace { get; init; }

    [IdentityPart(1)]
    [Predicate("name")]
    public partial string Name { get; init; }

    [Predicate("description")]
    public string? Description { get; set; }
}

