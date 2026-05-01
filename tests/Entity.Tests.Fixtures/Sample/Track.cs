using Forge.Entity;

namespace Forge.Entity.Tests.Fixtures.Sample;

/// <summary>
/// Random (UuidV4) entity. <see cref="PerformedBy"/> is the <b>N:1</b> side: many
/// tracks can be performed by the same Artist.
/// </summary>
[Entity(Path = "tracks", PredicatePath = "track")]
[Identity(IdentityGenerator.Random)]
public partial class Track
{
    [Predicate("title")]
    public string Title { get; set; } = "";

    [Predicate("position")]
    public int Position { get; set; }

    [Predicate("durationSeconds")]
    public int DurationSeconds { get; set; }

    /// <summary>N:1 — many tracks performed by the same artist.</summary>
    [Owning("performedBy")]
    public partial EntityRef<Artist>? PerformedBy { get; set; }
}
