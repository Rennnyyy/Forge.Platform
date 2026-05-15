using Forge.Entity;
using Forge.Operations;
using Forge.Structure;

namespace Forge.Application.Sample;

/// <summary>
/// A geometry node that holds a 2D SVG shape fragment and participates in the
/// product-structure scene graph. Geometry nodes are attached to structural nodes
/// (or to other <see cref="Geometry"/> nodes) via <see cref="GeometryUsage"/> edges
/// that carry a 2D affine transformation matrix.
/// <para>
/// In the car demo the geometry layer assembles a 2D top-down illustration of the
/// active structural configuration. Each structural node (typically a leaf) contributes
/// one or more geometry nodes. Their placements — stored on the
/// <see cref="GeometryUsage"/> edges — define how the geometry is positioned on a
/// shared SVG canvas (<c>viewBox="0 0 560 230"</c>).
/// </para>
/// <para>
/// Geometry nodes are <b>not</b> traversed by
/// <c>structure.configured-tree.get</c>: that capability only follows
/// <see cref="Usage"/> edges between <see cref="IStructure"/> nodes.
/// The geometry layer is queried separately via the standard CRUD endpoints
/// (<c>GET /api/entities/geometry-nodes</c> and
/// <c>GET /api/entities/geometry-usages</c>) and assembled client-side.
/// </para>
/// </summary>
/// <remarks>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/geometry-nodes</term><description>Create</description></item>
///   <item><term>GET    api/entities/geometry-nodes</term><description>List</description></item>
///   <item><term>GET    api/entities/geometry-nodes?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/geometry-nodes?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/geometry-nodes?iri=…</term><description>Delete</description></item>
/// </list>
/// </remarks>
[Entity(Path = "geometry-nodes")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Geometry : IStructure
{
    /// <summary>Human-readable label for this geometry node.</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description of this shape and its role.</summary>
    [Predicate("description")]
    public string? Description { get; set; }

    /// <summary>
    /// An inline SVG fragment (without the outer <c>&lt;svg&gt;</c> element) that
    /// represents this geometry in its own local coordinate space. Use
    /// <c>stroke="currentColor"</c> to allow the rendering context to control colour.
    /// <para>
    /// Coordinates are expressed in the geometry's <em>local</em> space; the
    /// <see cref="GeometryUsage.Matrix"/> on the parent <see cref="GeometryUsage"/> edge
    /// transforms them into the parent's coordinate space.
    /// </para>
    /// </summary>
    [Predicate("svgContent")]
    public string? SvgContent { get; set; }
}
