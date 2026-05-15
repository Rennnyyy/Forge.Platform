using System.Text.Json.Serialization;

namespace Forge.Structure;

/// <summary>
/// A single applicability condition evaluated against a <see cref="StructureConfiguration"/>.
/// See Structure ADR-0002 for the full condition taxonomy.
/// <para>
/// The <c>[JsonPolymorphic]</c> attribute enables round-trip JSON serialisation of
/// <see cref="Usage.Conditions"/> through the HTTP CRUD operations. The <c>"$type"</c>
/// discriminator values are: <c>"flag"</c>, <c>"enumeration"</c>, <c>"time"</c>.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FlagOptionCondition),        "flag")]
[JsonDerivedType(typeof(EnumerationOptionCondition), "enumeration")]
[JsonDerivedType(typeof(TimeCondition),              "time")]
public interface IStructureCondition
{
    /// <summary>
    /// Returns <see langword="true"/> when this condition is satisfied by
    /// <paramref name="config"/>; <see langword="false"/> otherwise.
    /// </summary>
    bool IsSatisfiedBy(StructureConfiguration config);
}
