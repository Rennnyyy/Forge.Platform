using Forge.Entity;

namespace Forge.Entity.Tests.Sample;

/// <summary>
/// Deterministic PropertyBasedEncoded (UuidV5) sample.
/// The namespace GUID is auto-derived from EntityOptions.BaseIri + "/widgets" at runtime,
/// so identity is scoped to the deployment base IRI without any hardcoded GUID.
/// </summary>
[Entity(Path = "widgets", PredicatePath = "widget")]
[Identity(IdentityGenerator.PropertyBasedEncoded)]
public partial class Widget
{
    [IdentityPart(0)]
    public partial string Code { get; init; }
}
