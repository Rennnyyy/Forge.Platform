namespace Forge.Aspects;

/// <summary>
/// An <see cref="IAspect"/> that carries SHACL shape material for write operations
/// (Create, Update, Delete). Resolved from <see cref="IAspectStore"/> by the
/// operation-aspect engine at transaction-commit time. See Aspects ADR-0001.
/// </summary>
public interface IOperationAspect : IAspect
{
    /// <summary>
    /// Turtle-serialized SHACL Local shape evaluated against a single-subject projection
    /// of the entity being written, or <c>null</c> if no local pass is required.
    /// </summary>
    string? LocalShapeTtl { get; }

    /// <summary>
    /// The body of the SPARQL <c>WHERE { }</c> block for the Context pass, or <c>null</c>
    /// if no context pass is required. The engine wraps this into a full SELECT and
    /// executes it against the transaction-local store state. Any row returned is a violation.
    /// <para>
    /// <c>?entityIri</c> is pre-bound to the IRI of the entity being operated on.
    /// <c>?focusNode</c>, <c>?message</c>, and <c>?path</c> are read from each violation row;
    /// <c>?focusNode</c> defaults to <c>?entityIri</c> when not bound by the query.
    /// </para>
    /// </summary>
    string? ContextWhere { get; }
}
