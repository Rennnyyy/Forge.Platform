using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// A directed placement edge from a parent node to a child <see cref="Geometry3D"/> node.
/// The edge carries a <b>4×4 column-major affine transformation matrix</b> that positions
/// the child in the parent's local coordinate space.
/// </summary>
/// <remarks>
/// <para>
/// Two edge kinds use this type:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Structure → Geometry3D</b>: <see cref="ParentIri"/> is the IRI of a structural
///       node; the matrix anchors the 3D scene root.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Geometry3D → Geometry3D</b>: <see cref="ParentIri"/> is the IRI of another
///       <see cref="Geometry3D"/> node; the matrix positions the child relative to its
///       parent's local coordinate space (scene-graph composition).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// The matrix is stored as a JSON-serialised 16-element array in <b>column-major order</b>
/// — the same layout used by WebGL, <c>THREE.Matrix4.fromArray()</c>, and glTF:
/// <code>
///   ⎡ m[0]  m[4]  m[8]  m[12] ⎤
///   ⎢ m[1]  m[5]  m[9]  m[13] ⎥
///   ⎢ m[2]  m[6] m[10]  m[14] ⎥
///   ⎣ m[3]  m[7] m[11]  m[15] ⎦
/// </code>
/// Identity: <c>[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]</c>.
/// A pure translation by (tx, ty, tz):
/// <c>[1,0,0,0, 0,1,0,0, 0,0,1,0, tx,ty,tz,1]</c>.
/// </para>
/// <para>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/geometry3d-usages</term><description>Create</description></item>
///   <item><term>GET    api/entities/geometry3d-usages</term><description>List</description></item>
///   <item><term>GET    api/entities/geometry3d-usages?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/geometry3d-usages?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/geometry3d-usages?iri=…</term><description>Delete</description></item>
/// </list>
/// </para>
/// </remarks>
[Entity(Path = "geometry3d-usages", PredicatePath = "geom3dUsage")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class GeometryUsage3D
{
    /// <summary>
    /// IRI of the parent — either a structural <see cref="Forge.Structure.IStructure"/>
    /// node or a <see cref="Geometry3D"/> node.
    /// </summary>
    [Predicate("parentIri")]
    public string ParentIri { get; set; } = string.Empty;

    /// <summary>IRI of the child <see cref="Geometry3D"/> node.</summary>
    [Predicate("childGeometry3dIri")]
    public string ChildGeometry3dIri { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialised form of <see cref="Matrix3d"/>. Stored in the RDF graph as a
    /// string literal via <c>[Predicate]</c>. Use <see cref="Matrix3d"/> for structured
    /// read/write access; do not set this property directly from application code.
    /// </summary>
    [Predicate("matrix3dJson")]
    [JsonIgnore]
    public string Matrix3dJson { get; set; } = "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]";

    /// <summary>
    /// The 16-element 4×4 affine matrix in <b>column-major order</b> (WebGL / glTF convention).
    /// Backed by <see cref="Matrix3dJson"/>: the setter serialises to JSON, the getter
    /// deserialises on demand. An absent or malformed JSON value yields the 4×4 identity.
    /// </summary>
    public double[] Matrix3d
    {
        get => string.IsNullOrEmpty(Matrix3dJson)
            ? Identity4x4
            : JsonSerializer.Deserialize<double[]>(Matrix3dJson) ?? Identity4x4;
        set => Matrix3dJson = JsonSerializer.Serialize(value);
    }

    private static readonly double[] Identity4x4 =
        [1, 0, 0, 0,  0, 1, 0, 0,  0, 0, 1, 0,  0, 0, 0, 1];
}
