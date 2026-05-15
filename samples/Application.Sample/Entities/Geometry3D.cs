using Forge.Entity;
using Forge.Operations;
using Forge.Structure;

namespace Forge.Application.Sample;

/// <summary>
/// A geometry node that holds a <b>3D shape in OBJ format</b> and participates in
/// the product-structure scene graph. <see cref="Geometry3D"/> nodes are placed via
/// <see cref="GeometryUsage3D"/> edges that carry a 4×4 affine transformation matrix
/// (column-major, identical to the WebGL / glTF convention).
/// </summary>
/// <remarks>
/// <para>
/// The shape is stored as a plain-text <b>Wavefront OBJ</b> fragment — a non-proprietary
/// ASCII format that can be read and edited in any text editor. A minimal box is ~15 lines;
/// a 3D preview is assembled client-side using Three.js by walking the
/// <see cref="GeometryUsage3D"/> hierarchy and applying each edge's 4×4 matrix.
/// </para>
/// <para>
/// Coordinate convention used in the car demo:
/// <list type="bullet">
///   <item><description>X — car length (front = −X)</description></item>
///   <item><description>Y — vertical (up = +Y)</description></item>
///   <item><description>Z — car width (right = +Z)</description></item>
/// </list>
/// </para>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/geometry3d-nodes</term><description>Create</description></item>
///   <item><term>GET    api/entities/geometry3d-nodes</term><description>List</description></item>
///   <item><term>GET    api/entities/geometry3d-nodes?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/geometry3d-nodes?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/geometry3d-nodes?iri=…</term><description>Delete</description></item>
/// </list>
/// </remarks>
[Entity(Path = "geometry3d-nodes")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Geometry3D : IStructure
{
    /// <summary>Human-readable label for this 3D geometry node.</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description of this shape and its role.</summary>
    [Predicate("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Wavefront OBJ text — a plain ASCII 3D mesh editable in any text editor.
    /// Only <c>v</c> (vertex) and <c>f</c> (face) lines are required; both triangles
    /// and quads are supported (quads are fan-triangulated client-side).
    /// The shape should be centred at the local origin; its placement in world space
    /// is controlled by the <see cref="GeometryUsage3D"/> edge that references it.
    /// </summary>
    [Predicate("objContent")]
    public string? ObjContent { get; set; }
}
