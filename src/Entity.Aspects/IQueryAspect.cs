using Forge.Entity.Repository;

namespace Forge.Entity.Aspects;

/// <summary>
/// An <see cref="IOperationAspect"/> that gates and validates read / query operations.
/// See Aspects ADR-0007.
/// </summary>
/// <remarks>
/// <para>
/// Two independent passes are executed by <see cref="IQueryAspectEngine"/>:
/// </para>
/// <list type="number">
///   <item>
///     <term>Access gate (Layer 1)</term>
///     <description>
///       <see cref="FilterWhere"/> is a SPARQL WHERE body fragment. For generated queries
///       (LINQ, type-scan) it is appended to the WHERE block before execution. For
///       expert-authored dynamic SPARQL it is substituted in place of the placeholder token
///       <c>##aspect:filter##</c>. For point reads (<c>LoadAsync</c>) a pre-load existence
///       check is executed using <see cref="ISparqlQueryStore"/>; zero rows → throws
///       <see cref="QueryAspectViolationException"/> (never returns silent null).
///     </description>
///   </item>
///   <item>
///     <term>Result-shape pass (Layer 2)</term>
///     <description>
///       <see cref="ResultShapeTtl"/> is validated once against the aggregate result graph
///       — all projected entity triples for the full result set. A single
///       <c>ShapesGraph.Validate</c> call covers cross-result invariants as well as
///       per-entity constraints.
///     </description>
///   </item>
/// </list>
/// </remarks>
public interface IQueryAspect : IOperationAspect
{
    /// <summary>
    /// SPARQL WHERE body fragment for the access gate / data filter, or <c>null</c> if no
    /// filter is required. The engine appends this to generated queries or substitutes it
    /// in place of the <c>##aspect:filter##</c> placeholder in expert-authored dynamic
    /// SPARQL. If non-null and the dynamic query lacks the placeholder, the engine throws
    /// <see cref="QueryAspectViolationException"/>.
    /// </summary>
    string? FilterWhere { get; }

    /// <summary>
    /// Turtle-serialized SHACL shape validated once against the aggregate result graph, or
    /// <c>null</c> if no output shape check is required. Uses <see cref="IShapeCache"/>
    /// (keyed by TTL hash) for efficient re-use.
    /// </summary>
    string? ResultShapeTtl { get; }
}
