using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// A <see cref="IOperationAspect"/> that holds shape data as inline strings.
/// Used for code-origin aspects registered via the DI extension or in tests.
/// </summary>
public sealed class InlineTtlWriteAspect : IOperationAspect
{
    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string? LocalShapeTtl { get; }

    /// <inheritdoc/>
    public string? ContextWhere { get; }

    /// <param name="name">Unique aspect name. Must not be <c>"noop"</c>.</param>
    /// <param name="localShapeTtl">Turtle-serialized SHACL Local shape, or <c>null</c>.</param>
    /// <param name="contextWhere">WHERE body for the Context pass SPARQL query, or <c>null</c>.
    /// Do not include the <c>SELECT</c> header or the <c>WHERE { }</c> delimiters — supply
    /// only the inner content. The engine projects <c>?focusNode</c>, <c>?message</c>, and
    /// <c>?path</c>.</param>
    public InlineTtlWriteAspect(string name, string? localShapeTtl, string? contextWhere)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name == "noop")
            throw new ArgumentException("The aspect name 'noop' is reserved.", nameof(name));

        Name = name;
        LocalShapeTtl = localShapeTtl;
        ContextWhere = contextWhere;
    }
}
