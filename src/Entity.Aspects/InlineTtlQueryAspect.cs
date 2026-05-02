namespace Forge.Entity.Aspects;

/// <summary>
/// A <see cref="IQueryAspect"/> that holds filter and shape data as inline strings.
/// Used for code-origin read aspects registered via the DI extension or in tests.
/// </summary>
public sealed class InlineTtlQueryAspect : IQueryAspect
{
    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string? FilterWhere { get; }

    /// <inheritdoc/>
    public string? ResultShapeTtl { get; }

    /// <param name="name">Unique aspect name. Must not be <c>"noop"</c>.</param>
    /// <param name="filterWhere">
    /// SPARQL WHERE body fragment for the access gate, or <c>null</c>. Do not include
    /// <c>WHERE { }</c> delimiters — supply only the inner content.
    /// </param>
    /// <param name="resultShapeTtl">
    /// Turtle-serialized SHACL shape for the result-graph pass, or <c>null</c>.
    /// </param>
    public InlineTtlQueryAspect(string name, string? filterWhere, string? resultShapeTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name == "noop")
            throw new ArgumentException("The aspect name 'noop' is reserved.", nameof(name));

        Name = name;
        FilterWhere = filterWhere;
        ResultShapeTtl = resultShapeTtl;
    }
}
