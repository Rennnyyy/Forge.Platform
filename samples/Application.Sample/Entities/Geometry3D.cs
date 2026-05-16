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
/// The OBJ mesh is stored as a <b>binary object</b> (plain-text <c>text/plain</c> content) in an
/// <c>IObjectStore</c> keyed <c>"geometry3d-obj"</c>. Metadata (name, description, object key)
/// lives in the RDF entity store. A 3D preview is assembled client-side using Three.js by
/// fetching each node's OBJ from <c>api/objects/geometry3d-nodes/content?iri=…</c>,
/// walking the <see cref="GeometryUsage3D"/> hierarchy, and applying each edge's 4×4 matrix.
/// </para>
/// <para>
/// Coordinate convention used in the car demo:
/// <list type="bullet">
///   <item><description>X — car length (front = −X)</description></item>
///   <item><description>Y — vertical (up = +Y)</description></item>
///   <item><description>Z — car width (right = +Z)</description></item>
/// </list>
/// </para>
/// <para>
/// All routes are owned by <c>MapObjectOperations()</c>; <c>MapOperations()</c> skips this type.
/// </para>
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/geometry3d-nodes</term><description>Create metadata entity</description></item>
///   <item><term>GET    api/entities/geometry3d-nodes</term><description>List metadata entities</description></item>
///   <item><term>GET    api/entities/geometry3d-nodes?iri=…</term><description>Read single metadata entity</description></item>
///   <item><term>PUT    api/entities/geometry3d-nodes?iri=…</term><description>Update metadata entity</description></item>
///   <item><term>DELETE api/entities/geometry3d-nodes?iri=…</term><description>Delete entity + blob (combined)</description></item>
///   <item><term>PUT    api/objects/geometry3d-nodes/content?iri=…</term><description>Upload OBJ content</description></item>
///   <item><term>GET    api/objects/geometry3d-nodes/content?iri=…</term><description>Download OBJ content</description></item>
///   <item><term>DELETE api/objects/geometry3d-nodes/content?iri=…</term><description>Delete blob only; entity stays</description></item>
/// </list>
/// <para>
/// <c>ObjectKey</c>, <c>ContentType</c>, and <c>ForgeObjectStoreKey</c> are emitted
/// by the generator; do not declare them manually.
/// </para>
/// </remarks>
[Entity(Path = "geometry3d-nodes")]
[Identity(IdentityGenerator.Random)]
[ObjectBearing("geometry3d-obj")]
public partial class Geometry3D : IStructure
{
    /// <summary>Human-readable label for this 3D geometry node.</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description of this shape and its role.</summary>
    [Predicate("description")]
    public string? Description { get; set; }
}
