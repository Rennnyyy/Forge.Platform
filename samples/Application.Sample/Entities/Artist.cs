using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// Sample entity demonstrating <c>Forge.Operations.Http</c>:
/// a direct REST CRUD surface without the Capability dispatch pipeline.
/// </summary>
/// <remarks>
/// Because <c>[Identity(IdentityGenerator.Random)]</c> is used, each POST request
/// generates a new UUID-based IRI automatically.  The entity does <em>not</em> carry
/// <c>[CrudCapabilities]</c>, showing the Operations.Http path in isolation.
/// <br/>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/artists</term><description>Create</description></item>
///   <item><term>GET    api/entities/artists</term><description>List</description></item>
///   <item><term>GET    api/entities/artists?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/artists?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/artists?iri=…</term><description>Delete</description></item>
/// </list>
/// </remarks>
[Entity(Path = "artists")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Artist
{
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    [Predicate("country")]
    public string Country { get; set; } = string.Empty;

    [Predicate("bio")]
    public string? Bio { get; set; }

    [Predicate("active")]
    public bool Active { get; set; } = true;

    [Predicate("debutYear")]
    public int DebutYear { get; set; }

    /// <summary>
    /// Inverse — the <see cref="Studio"/> entities managed by this artist.
    /// Populated at load time via the inverse-ref loader (ADR-0018).
    /// Read-only; mutate via <see cref="Studio.ManagedBy"/>.
    /// <br/>
    /// Eager (non-lazy) — always included in both list and single-read responses.
    /// </summary>
    [Inverse(nameof(Studio.ManagedBy), "managedBy")]
    public partial EntityRefCollection<Studio> ManagedStudios { get; }
}
