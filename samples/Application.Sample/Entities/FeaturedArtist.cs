using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// Entity subtype of <see cref="Artist"/> demonstrating ADR-0016 entity type inheritance.
/// </summary>
/// <remarks>
/// <c>FeaturedArtist</c> inherits the <c>UuidV4</c> identity strategy and all scalar
/// predicates from <c>Artist</c>.  It extends the model with two predicates scoped
/// under the <c>featured-artist</c> predicate path.
/// <br/>
/// <c>[OperationEndpoints("featured-artists")]</c> supplies the route segment explicitly
/// because <c>Path</c> on child <c>[Entity]</c> attributes is forbidden (FORGE0007).
/// The registered endpoints are:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/featured-artists</term><description>Create</description></item>
///   <item><term>GET    api/entities/featured-artists</term><description>List</description></item>
///   <item><term>GET    api/entities/featured-artists?iri=…</term><description>Read</description></item>
///   <item><term>PUT    api/entities/featured-artists?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/featured-artists?iri=…</term><description>Delete</description></item>
/// </list>
/// Because a <c>FeaturedArtist</c> is an <c>Artist</c> at the RDF level (multi-type
/// <c>rdf:type</c> projection from ADR-0016), a <c>GET api/entities/artists</c> list
/// also returns featured-artist instances — proven by chapter 14 of the Bruno collection.
/// </remarks>
[Entity(PredicatePath = "featured-artist")]
[OperationEndpoints("featured-artists")]
public partial class FeaturedArtist : Artist
{
    /// <summary>Calendar year from which the artist is featured on the platform.</summary>
    [Predicate("featuredSince")]
    public int FeaturedSince { get; set; }

    /// <summary>Optional sponsor name; absent when self-managed.</summary>
    [Predicate("sponsorName")]
    public string? SponsorName { get; set; }
}
