using Forge.Entity;

namespace Forge.Entity.Tests.Fixtures.Sample;

/// <summary>
/// Random (UuidV4) entity.
/// <list type="bullet">
///   <item><see cref="ReleasedBy"/> — N:1 to Label (many albums per label)</item>
///   <item><see cref="Artists"/>    — M:N owning side  (same Artist IRI on multiple Albums)</item>
///   <item><see cref="Tracks"/>     — 1:N ordered rdf:List (one album → many tracks)</item>
/// </list>
/// </summary>
[Entity(Path = "albums", PredicatePath = "album")]
[Identity(IdentityGenerator.Random)]
public partial class Album
{
    [Predicate("title")]
    public string Title { get; set; } = "";

    [Predicate("releaseYear")]
    public int ReleaseYear { get; set; }

    [Predicate("explicit")]
    public bool Explicit { get; set; }

    /// <summary>N:1 — many albums per label.</summary>
    [Owning("releasedBy")]
    public partial EntityRef<Label>? ReleasedBy { get; set; }

    /// <summary>M:N owning side — same Artist can appear on multiple Albums.</summary>
    [Owning("hasArtist")]
    public partial EntityRefCollection<Artist> Artists { get; }

    /// <summary>1:N ordered — tracks in disc-position order.</summary>
    [Owning("hasTrack")]
    public partial EntityRefCollection<Track> Tracks { get; }
}
