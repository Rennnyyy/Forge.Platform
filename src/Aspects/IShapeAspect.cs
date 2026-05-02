using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// An <see cref="Forge.Repository.IOperationAspect"/> that carries SHACL shape material.
/// The engine casts <c>IOperationAspect</c> to this interface to obtain shape data; the cast
/// succeeds for every non-NoOp aspect registered in Trunk 2.
/// See Aspects ADR-0004, ADR-0006.
/// </summary>
public interface IShapeAspect : Forge.Repository.IOperationAspect
{
    /// <summary>
    /// Turtle-serialized SHACL Local shape, or <c>null</c> if this aspect has no local pass.
    /// The shape is evaluated against a single-subject projection of the entity being written.
    /// </summary>
    string? LocalShapeTtl { get; }

    /// <summary>
    /// The body of the SPARQL <c>WHERE { }</c> block for the Context pass, or <c>null</c> if
    /// this aspect has no context pass. The engine wraps this into a full
    /// <c>SELECT ?focusNode ?message ?path WHERE { VALUES ?entityIri { &lt;iri&gt; } … }</c>
    /// query and executes it via
    /// <see cref="Forge.Repository.ISparqlQueryStore"/> against the transaction-local
    /// store state. Any row returned is treated as a violation.
    /// <para>
    /// <c>?entityIri</c> is pre-bound to the IRI of the entity being operated on.
    /// <c>?focusNode</c>, <c>?message</c>, and <c>?path</c> are read from each violation row;
    /// <c>?focusNode</c> defaults to <c>?entityIri</c> when not bound by the query.
    /// </para>
    /// </summary>
    string? ContextWhere { get; }
}
