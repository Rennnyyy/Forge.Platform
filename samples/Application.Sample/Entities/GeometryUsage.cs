using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// A directed placement edge from a parent — either a structural
/// <see cref="Forge.Structure.Node"/> (or any <see cref="Forge.Structure.IStructure"/>
/// node) or another <see cref="Geometry"/> — to a child <see cref="Geometry"/> node.
/// The edge carries a 2D affine transformation matrix that positions the child geometry
/// shapes inside the parent's coordinate space.
/// </summary>
/// <remarks>
/// <para>
/// Two edge kinds use this type:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Structure → Geometry</b>: <see cref="ParentIri"/> holds the IRI of a
///       structural node (e.g. a <see cref="Forge.Structure.Node"/>); the matrix
///       positions the child geometry on the shared assembly canvas.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Geometry → Geometry</b>: <see cref="ParentIri"/> holds the IRI of a
///       <see cref="Geometry"/> node; the matrix positions the child geometry
///       <em>relative to the parent geometry's local coordinate space</em>, enabling
///       hierarchical scene-graph composition (e.g. wheels placed relative to a car body).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// The matrix is stored as the JSON-serialised array <c>[a, b, c, d, e, f]</c> —
/// following the SVG / Canvas 2D convention:
/// <code>
///   ⎡ a  c  e ⎤
///   ⎣ b  d  f ⎦
/// </code>
/// The identity is <c>[1, 0, 0, 1, 0, 0]</c>; a pure translation by (tx, ty) is
/// <c>[1, 0, 0, 1, tx, ty]</c>.
/// </para>
/// <para>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/geometry-usages</term><description>Create</description></item>
///   <item><term>GET    api/entities/geometry-usages</term><description>List</description></item>
///   <item><term>GET    api/entities/geometry-usages?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/geometry-usages?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/geometry-usages?iri=…</term><description>Delete</description></item>
/// </list>
/// </para>
/// </remarks>
[Entity(Path = "geometry-usages", PredicatePath = "geomUsage")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class GeometryUsage
{
    /// <summary>
    /// IRI of the parent — either a structural <see cref="Forge.Structure.IStructure"/>
    /// node or a <see cref="Geometry"/> node.
    /// </summary>
    [Predicate("parentIri")]
    public string ParentIri { get; set; } = string.Empty;

    /// <summary>IRI of the child <see cref="Geometry"/> node.</summary>
    [Predicate("childGeometryIri")]
    public string ChildGeometryIri { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialised form of <see cref="Matrix"/>. Stored in the RDF graph as a
    /// string literal via <c>[Predicate]</c>. Use <see cref="Matrix"/> for structured
    /// read/write access; do not set this property directly from application code.
    /// </summary>
    [Predicate("matrixJson")]
    [JsonIgnore]
    public string MatrixJson { get; set; } = "[1,0,0,1,0,0]";

    /// <summary>
    /// The 6-element 2D affine transformation matrix <c>[a, b, c, d, e, f]</c>.
    /// Backed by <see cref="MatrixJson"/>: the setter serialises to JSON, the getter
    /// deserialises on demand. An absent or malformed JSON value yields the
    /// identity matrix <c>[1, 0, 0, 1, 0, 0]</c>.
    /// </summary>
    public double[] Matrix
    {
        get => string.IsNullOrEmpty(MatrixJson)
            ? [1.0, 0.0, 0.0, 1.0, 0.0, 0.0]
            : JsonSerializer.Deserialize<double[]>(MatrixJson) ?? [1.0, 0.0, 0.0, 1.0, 0.0, 0.0];
        set => MatrixJson = JsonSerializer.Serialize(value);
    }
}
