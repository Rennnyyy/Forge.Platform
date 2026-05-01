using Forge.Entity.Repository.Rdf;

namespace Forge.Entity.Repository;

/// <summary>Well-known RDF/XSD IRIs used by the default mapper.</summary>
public static class RdfVocab
{
    public const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    public const string Type  = Rdf + "type";
    public const string First = Rdf + "first";
    public const string Rest  = Rdf + "rest";
    public const string Nil   = Rdf + "nil";

    public const string XsdString   = Xsd + "string";
    public const string XsdBoolean  = Xsd + "boolean";
    public const string XsdInt      = Xsd + "int";
    public const string XsdLong     = Xsd + "long";
    public const string XsdDecimal  = Xsd + "decimal";
    public const string XsdDouble   = Xsd + "double";
    public const string XsdFloat    = Xsd + "float";
    public const string XsdDateTime = Xsd + "dateTime";
    public const string XsdDate     = Xsd + "date";
    public const string XsdTime     = Xsd + "time";

    public static RdfTerm RdfTypeIri => RdfTerm.Iri(Type);
    public static RdfTerm RdfFirstIri => RdfTerm.Iri(First);
    public static RdfTerm RdfRestIri => RdfTerm.Iri(Rest);
    public static RdfTerm RdfNilIri => RdfTerm.Iri(Nil);
}
