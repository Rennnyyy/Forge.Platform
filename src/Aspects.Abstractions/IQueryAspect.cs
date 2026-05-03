namespace Forge.Aspects;

/// <summary>
/// An <see cref="IAspect"/> that gates and validates read/query operations.
/// Resolved from <see cref="IAspectStore"/> by the query-aspect engine. See Aspects ADR-0007.
/// </summary>
public interface IQueryAspect : IAspect
{
    /// <summary>
    /// SPARQL WHERE body fragment for the access gate, or <c>null</c> if no filter is required.
    /// Appended to generated queries or substituted via the <c>##aspect:filter##</c>
    /// placeholder in expert-authored dynamic SPARQL.
    /// </summary>
    string? FilterWhere { get; }

    /// <summary>
    /// Turtle-serialized SHACL shape validated once against the aggregate result graph, or
    /// <c>null</c> if no output shape check is required.
    /// </summary>
    string? ResultShapeTtl { get; }
}
