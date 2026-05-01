namespace Forge.Entity.Repository.Rdf;

/// <summary>An RDF triple: subject (IRI or blank), predicate (IRI), object (any term).</summary>
public readonly record struct RdfTriple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)
{
    public override string ToString() =>
        $"{FormatTerm(Subject)} {FormatTerm(Predicate)} {FormatTerm(Object)} .";

    private static string FormatTerm(RdfTerm t) => t.Kind switch
    {
        RdfTermKind.Iri => $"<{t.Value}>",
        RdfTermKind.BlankNode => $"_:{t.Value}",
        RdfTermKind.Literal when t.Language is not null => $"\"{t.Value}\"@{t.Language}",
        RdfTermKind.Literal when t.DatatypeIri is not null => $"\"{t.Value}\"^^<{t.DatatypeIri}>",
        RdfTermKind.Literal => $"\"{t.Value}\"",
        _ => t.Value,
    };
}
