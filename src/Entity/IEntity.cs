namespace Forge.Entity;

/// <summary>
/// Implemented by every entity. The implementation is normally emitted by the source generator
/// onto the user's <c>partial class</c>.
/// </summary>
public interface IEntity
{
    /// <summary>The materialized IRI of this entity. Sealed once assigned.</summary>
    string Iri { get; }

    /// <summary>True once <see cref="Iri"/> has been computed/assigned.</summary>
    bool IsIdentitySealed { get; }
}
