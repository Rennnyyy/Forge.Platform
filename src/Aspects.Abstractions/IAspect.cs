namespace Forge.Aspects;

/// <summary>
/// Marker for a named validation policy. The IRI is the canonical identity key
/// used across all legs (operation, query, message) of the Aspects infrastructure.
/// Use <see cref="Aspect.NoOpIri"/> to declare that no validation applies.
/// </summary>
public interface IAspect
{
    /// <summary>
    /// The canonical IRI that uniquely identifies this aspect.
    /// The value <see cref="Aspect.NoOpIri"/> is reserved for the no-operation sentinel.
    /// </summary>
    string Iri { get; }
}
