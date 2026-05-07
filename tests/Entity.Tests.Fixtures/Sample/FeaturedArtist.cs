using Forge.Entity;

namespace Forge.Entity.Tests.Fixtures.Sample;

/// <summary>
/// Entity subtype of <see cref="Artist"/> (ADR-0016).
/// Inherits the UuidV5 identity strategy (Name + Country identity parts) and all of
/// Artist's scalar predicates. Declares two additional predicates scoped under the
/// <c>featured-artist</c> predicate path.
/// </summary>
[Entity(PredicatePath = "featured-artist")]
public partial class FeaturedArtist : Artist
{
    /// <summary>Calendar year from which the artist is featured.</summary>
    [Predicate("featuredSince")]
    public int FeaturedSince { get; set; }

    /// <summary>Optional sponsor name — absent when self-managed.</summary>
    [Predicate("sponsorName")]
    public string? SponsorName { get; set; }
}
