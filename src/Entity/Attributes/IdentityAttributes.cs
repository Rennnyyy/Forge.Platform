namespace Forge.Entity;

/// <summary>
/// Strategy used by the source generator to derive an entity's IRI suffix.
/// </summary>
public enum IdentityGenerator
{
    /// <summary>Concatenate <see cref="IdentityPartAttribute"/>-marked properties (ordered) with a separator.</summary>
    PropertyBasedPlain = 0,

    /// <summary>Random GUIDv4. Persisted across hydration via a private backing field.</summary>
    Random = 1,

    /// <summary>
    /// Deterministic GUIDv5 hashed from <see cref="IdentityPartAttribute"/>-marked properties
    /// under the namespace declared on <see cref="IdentityAttribute.Namespace"/>.
    /// </summary>
    PropertyBasedEncoded = 2,
}

/// <summary>
/// Configures the IRI generator for an Entity class. Exactly one
/// <see cref="IdentityAttribute"/> is required per <see cref="EntityAttribute"/>; an analyzer enforces this.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IdentityAttribute : Attribute
{
    public IdentityGenerator Generator { get; }

    /// <summary>
    /// Optional for <see cref="IdentityGenerator.PropertyBasedEncoded"/>; ignored otherwise.
    /// When set, must be a parseable RFC 4122 GUID and is used as the UUIDv5 namespace for hashing.
    /// When omitted, the namespace is derived at runtime as
    /// <c>UuidV5(RFC4122-URL-namespace, EntityOptions.BaseIri + "/" + [Entity(Path)])</c>,
    /// so the identity space is scoped to the deployment base IRI automatically.
    /// </summary>
    public string? Namespace { get; init; }

    public IdentityAttribute(IdentityGenerator generator) => Generator = generator;
}

/// <summary>
/// Marks a property as a participant in identity generation.
/// Required for <see cref="IdentityGenerator.PropertyBasedPlain"/> and <see cref="IdentityGenerator.PropertyBasedEncoded"/>;
/// ignored for <see cref="IdentityGenerator.Random"/>.
///
/// Allowed property kinds (analyzer-enforced):
///   - primitive (string, numeric, bool, Guid, DateTime*, Uri)
///   - List of primitive (joined by <see cref="Separator"/>)
///   - Reference to another entity (uses that entity's IRI)
///   - List of references (joined by <see cref="Separator"/>)
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class IdentityPartAttribute : Attribute
{
    public int Order { get; }

    /// <summary>Separator used when joining list values. Default <c>"/"</c>.</summary>
    public string Separator { get; init; } = "-";

    public IdentityPartAttribute(int order) => Order = order;
}
