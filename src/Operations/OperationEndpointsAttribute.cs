namespace Forge.Operations;

/// <summary>
/// Marks an entity class as having standard REST operation endpoints registered by
/// <c>MapOperations()</c> from <c>Forge.Operations.Http</c>.
/// </summary>
/// <remarks>
/// Place this attribute alongside <c>[Entity]</c> and <c>[Identity]</c> on an entity class.
/// <c>AddOperationEndpointsHttp()</c> discovers annotated types by assembly scanning and
/// wires five endpoints per entity:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/{path}</term><description>Create</description></item>
///   <item><term>GET    api/entities/{path}</term><description>List</description></item>
///   <item><term>GET    api/entities/{path}?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/{path}?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/{path}?iri=…</term><description>Delete</description></item>
/// </list>
/// CUD operations run through <c>EntityTransaction</c> so any registered
/// <c>IOperationAspect</c> is applied. The operation-aspect IRI is forwarded from the
/// <c>X-Forge-Operation-AspectIri</c> request header.
/// </remarks>
/// <param name="path">
/// Optional route segment override.  When <c>null</c> (default) the path is taken from the
/// enclosing <c>[Entity(Path = …)]</c>.  Must not contain slashes.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OperationEndpointsAttribute(string? path = null) : Attribute
{
    /// <summary>
    /// Route-segment override, or <c>null</c> if <c>[Entity(Path = …)]</c> should be used.
    /// </summary>
    public string? Path { get; } = path;
}
