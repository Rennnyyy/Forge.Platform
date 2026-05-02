namespace Forge.Repository.Rdf;

/// <summary>
/// The kind of RDF term: a node IRI, a blank node, or a literal value.
/// </summary>
public enum RdfTermKind { Iri, BlankNode, Literal }

/// <summary>
/// A minimal RDF term. Allocation-light value type; carries either an IRI/blank-node
/// label or a literal lexical form with optional datatype IRI and language tag.
/// </summary>
public readonly record struct RdfTerm(
    RdfTermKind Kind,
    string Value,
    string? DatatypeIri = null,
    string? Language = null)
{
    public static RdfTerm Iri(string iri) => new(RdfTermKind.Iri, iri);
    public static RdfTerm Blank(string label) => new(RdfTermKind.BlankNode, label);
    public static RdfTerm Literal(string lex, string? datatypeIri = null, string? language = null) =>
        new(RdfTermKind.Literal, lex, datatypeIri, language);

    public bool IsIri => Kind == RdfTermKind.Iri;
    public bool IsLiteral => Kind == RdfTermKind.Literal;
    public bool IsBlank => Kind == RdfTermKind.BlankNode;
}
