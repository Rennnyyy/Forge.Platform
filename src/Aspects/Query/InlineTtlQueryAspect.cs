namespace Forge.Aspects.Query;

/// <summary>
/// A <see cref="IQueryAspect"/> that holds filter and shape data as inline strings.
/// Used for code-origin read aspects registered via the DI extension or in tests.
/// </summary>
public sealed class InlineTtlQueryAspect : IQueryAspect
{
    /// <inheritdoc/>
    public string Iri { get; }

    /// <inheritdoc/>
    public string? FilterWhere { get; }

    /// <inheritdoc/>
    public string? ResultShapeTtl { get; }

    /// <param name="iri">Canonical IRI identifying this aspect. Must not be <see cref="Aspect.NoOpIri"/>.</param>
    /// <param name="filterWhere">
    /// SPARQL WHERE body fragment for the access gate, or <c>null</c>. Do not include
    /// <c>WHERE { }</c> delimiters — supply only the inner content.
    /// </param>
    /// <param name="resultShapeTtl">
    /// Turtle-serialized SHACL shape for the result-graph pass, or <c>null</c>.
    /// </param>
    public InlineTtlQueryAspect(string iri, string? filterWhere, string? resultShapeTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        if (iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved.", nameof(iri));

        Iri = iri;
        FilterWhere = filterWhere;
        ResultShapeTtl = resultShapeTtl;
    }
}
