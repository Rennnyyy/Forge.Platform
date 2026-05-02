namespace Forge.Entity.Aspects;

/// <summary>
/// A single SHACL constraint violation returned by the Aspects engine.
/// </summary>
public sealed record AspectViolation(
    /// <summary>IRI of the focus node that violated the constraint.</summary>
    string FocusNodeIri,

    /// <summary>Predicate path that was violated, or <c>null</c> for node-level constraints.</summary>
    string? PathPredicate,

    /// <summary>SHACL severity IRI (e.g. <c>http://www.w3.org/ns/shacl#Violation</c>).</summary>
    string Severity,

    /// <summary>Human-readable constraint message.</summary>
    string Message,

    /// <summary>IRI of the source SHACL shape, or <c>null</c> if unavailable.</summary>
    string? SourceShapeIri);
