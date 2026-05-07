using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// An individual recorded session produced inside a <see cref="Studio"/>.
/// Referenced 1:N from <see cref="Studio.Recordings"/> — one studio produces many recordings.
/// <br/>
/// Uses <c>[Identity(IdentityGenerator.Random)]</c> so each POST request generates a new
/// UUID-based IRI automatically.
/// <br/>
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/recordings</term><description>Create</description></item>
///   <item><term>GET    api/entities/recordings</term><description>List / Read</description></item>
///   <item><term>PUT    api/entities/recordings?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/recordings?iri=…</term><description>Delete</description></item>
/// </list>
/// </summary>
[Entity(Path = "recordings", PredicatePath = "recording")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Recording
{
    /// <summary>Title of the recorded session or track.</summary>
    [Predicate("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Total duration of the recording in seconds.</summary>
    [Predicate("durationSeconds")]
    public int DurationSeconds { get; set; }

    /// <summary>Calendar date on which the recording session took place.</summary>
    [Predicate("recordedOn")]
    public DateOnly RecordedOn { get; set; }

    /// <summary><see langword="true"/> when this was a live session; <see langword="false"/> for studio takes.</summary>
    [Predicate("isLive")]
    public bool IsLive { get; set; }

    /// <summary>Optional position within the studio's recording sequence.</summary>
    [Predicate("trackNumber")]
    public int? TrackNumber { get; set; }
}
