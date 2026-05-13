using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// A master audio file asset associated with a recording session.
/// Demonstrates the <c>[ObjectBearing]</c> pattern: the entity carries metadata
/// in the RDF store while the binary audio data lives in an <c>IObjectStore</c>.
/// <br/>
/// All routes are owned by <c>MapObjectOperations()</c>; <c>MapOperations()</c> skips this type.
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/track-masters</term><description>Create metadata entity</description></item>
///   <item><term>GET    api/entities/track-masters</term><description>List metadata entities</description></item>
///   <item><term>GET    api/entities/track-masters?iri=…</term><description>Read single metadata entity</description></item>
///   <item><term>PUT    api/entities/track-masters?iri=…</term><description>Update metadata entity</description></item>
///   <item><term>DELETE api/entities/track-masters?iri=…</term><description>Delete entity + blob (combined)</description></item>
///   <item><term>PUT    api/objects/track-masters/content?iri=…</term><description>Upload audio file (saga)</description></item>
///   <item><term>GET    api/objects/track-masters/content?iri=…</term><description>Download audio file</description></item>
///   <item><term>DELETE api/objects/track-masters/content?iri=…</term><description>Delete blob only; entity stays</description></item>
/// </list>
/// <c>ObjectKey</c>, <c>ContentType</c>, and <c>ForgeObjectStoreKey</c> are emitted
/// by the generator; do not declare them manually.
/// </summary>
[Entity(Path = "track-masters", PredicatePath = "trackMaster")]
[Identity(IdentityGenerator.Random)]
[ObjectBearing("track-master-audio")]
public partial class TrackMaster
{
    /// <summary>Human-readable label for this master recording asset.</summary>
    [Predicate("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Audio format description, e.g. "WAV 24-bit 96kHz".</summary>
    [Predicate("format")]
    public string? Format { get; set; }

    /// <summary>Notes from the recording engineer about this master take.</summary>
    [Predicate("engineerNotes")]
    public string? EngineerNotes { get; set; }
}
