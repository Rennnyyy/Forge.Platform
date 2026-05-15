using Forge.Entity;
using Forge.Operations;

namespace Forge.Structure;

/// <summary>
/// A generic structural node in a variant DAG. Implements <see cref="IStructure"/> so
/// that <see cref="Usage"/> edges can connect <see cref="Node"/> instances.
/// <para>
/// Applications that need richer node properties can define their own entity type and
/// implement <see cref="IStructure"/> on it; <see cref="Node"/> is the zero-friction
/// default for cases where a name and an optional description are sufficient.
/// </para>
/// <remarks>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/structure-nodes</term><description>Create</description></item>
///   <item><term>GET    api/entities/structure-nodes</term><description>List</description></item>
///   <item><term>GET    api/entities/structure-nodes?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/structure-nodes?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/structure-nodes?iri=…</term><description>Delete</description></item>
/// </list>
/// See Structure ADR-0001.
/// </remarks>
[Entity(Path = "structure-nodes")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Node : IStructure
{
    /// <summary>Human-readable label for this structural node.</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description.</summary>
    [Predicate("description")]
    public string? Description { get; set; }
}
