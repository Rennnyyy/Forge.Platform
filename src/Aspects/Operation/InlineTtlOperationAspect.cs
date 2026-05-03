namespace Forge.Aspects.Operation;

/// <summary>
/// An <see cref="IOperationAspect"/> that holds shape data as inline strings.
/// Used for code-origin aspects registered via the DI extension or in tests.
/// </summary>
public sealed class InlineTtlOperationAspect : IOperationAspect
{
    /// <inheritdoc/>
    public string Iri { get; }

    /// <inheritdoc/>
    public string? LocalShapeTtl { get; }

    /// <inheritdoc/>
    public string? ContextWhere { get; }

    /// <param name="iri">Unique aspect IRI. Must not be <see cref="Aspect.NoOpIri"/>.</param>
    /// <param name="localShapeTtl">Turtle-serialized SHACL Local shape, or <c>null</c>.</param>
    /// <param name="contextWhere">WHERE body for the Context pass SPARQL query, or <c>null</c>.
    /// Do not include the <c>SELECT</c> header or the <c>WHERE { }</c> delimiters.</param>
    public InlineTtlOperationAspect(string iri, string? localShapeTtl, string? contextWhere)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        if (iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved.", nameof(iri));

        Iri = iri;
        LocalShapeTtl = localShapeTtl;
        ContextWhere = contextWhere;
    }
}
