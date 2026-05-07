using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// <strong>Overly-complex demonstration entity</strong> that combines every supported
/// scalar CLR type (non-nullable and nullable) with all three owned-relation flavours:
///
/// <list type="table">
///   <listheader><term>Relation</term><description>Flavour</description></listheader>
///   <item>
///     <term><see cref="ManagedBy"/></term>
///     <description>
///       N:1 — many studios can be managed by the same <see cref="Artist"/>.
///       Stored as a single IRI in this entity's RDF graph.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Recordings"/></term>
///     <description>
///       1:N — one studio produces an ordered collection of <see cref="Recording"/> entities.
///       Stored as an <c>rdf:List</c> in this entity's graph.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Genres"/></term>
///     <description>
///       M:N — a studio specialises in multiple genres; the same <see cref="Genre"/>
///       IRI may appear in the <c>hasGenre</c> list of many studios.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Owned relations are bound from the JSON request body alongside scalars:
/// <c>managedBy</c> accepts an IRI string, <c>recordings</c> and <c>genres</c>
/// accept arrays of IRI strings. The GET / List responses serialize them in the same
/// shape. See <a href="../adr/0006-genre-enumeration-and-relation-demo.md">sample ADR-0006</a>.
/// </para>
///
/// Endpoints registered by <c>MapOperations()</c>:
/// <list type="table">
///   <listheader><term>Verb + Route</term><description>Operation</description></listheader>
///   <item><term>POST   api/entities/studios</term><description>Create</description></item>
///   <item><term>GET    api/entities/studios</term><description>List / Read</description></item>
///   <item><term>PUT    api/entities/studios?iri=…</term><description>Update</description></item>
///   <item><term>DELETE api/entities/studios?iri=…</term><description>Delete</description></item>
/// </list>
/// </summary>
[Entity(Path = "studios", PredicatePath = "studio")]
[Identity(IdentityGenerator.Random)]
[OperationEndpoints]
public partial class Studio
{
    // ── Non-nullable scalar properties ───────────────────────────────────────
    // Every supported CLR scalar type is represented exactly once.

    /// <summary>Display name of the studio.</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary><see langword="true"/> while the studio is actively accepting bookings.</summary>
    [Predicate("active")]
    public bool Active { get; set; }

    /// <summary>Calendar year in which the studio opened (e.g. 2001).</summary>
    [Predicate("foundedYear")]
    public int FoundedYear { get; set; }

    /// <summary>Cumulative number of recording sessions completed (64-bit counter).</summary>
    [Predicate("sessionCount")]
    public long SessionCount { get; set; }

    /// <summary>Average acoustic quality rating on a 0–10 scale (single-precision).</summary>
    [Predicate("acousticRating")]
    public float AcousticRating { get; set; }

    /// <summary>Composite reputation score computed from reviews (double-precision).</summary>
    [Predicate("reputationScore")]
    public double ReputationScore { get; set; }

    /// <summary>Annual operating budget in the studio's local currency (exact decimal).</summary>
    [Predicate("budget")]
    public decimal Budget { get; set; }

    /// <summary>Date the studio officially opened its doors.</summary>
    [Predicate("openedOn")]
    public DateOnly OpenedOn { get; set; }

    /// <summary>Timestamp of the most recently completed booking.</summary>
    [Predicate("lastBookedAt")]
    public DateTimeOffset LastBookedAt { get; set; }

    /// <summary>Stable external identifier used for third-party integrations.</summary>
    [Predicate("externalId")]
    public Guid ExternalId { get; set; }

    /// <summary>Canonical website URL of the studio.</summary>
    [Predicate("website")]
    public Uri Website { get; set; } = new Uri("https://forge-it.net/");

    // ── Nullable scalar properties ────────────────────────────────────────────
    // Same type coverage as above; each may be absent in the graph.

    /// <summary>Free-text biography or marketing description (optional).</summary>
    [Predicate("bio")]
    public string? Bio { get; set; }

    /// <summary>
    /// Verification flag set by a platform administrator.
    /// <see langword="null"/> means "not yet reviewed".
    /// </summary>
    [Predicate("verified")]
    public bool? Verified { get; set; }

    /// <summary>Maximum simultaneous occupancy (people), if the studio tracks capacity.</summary>
    [Predicate("capacity")]
    public int? Capacity { get; set; }

    /// <summary>Total number of tracks in the studio's back-catalogue (optional 64-bit).</summary>
    [Predicate("catalogueSize")]
    public long? CatalogueSize { get; set; }

    /// <summary>Optional override for the acoustic isolation score (single-precision).</summary>
    [Predicate("isolationFactor")]
    public float? IsolationFactor { get; set; }

    /// <summary>Gross profit margin as a fraction (0.0–1.0); absent until the first fiscal year closes.</summary>
    [Predicate("profitMargin")]
    public double? ProfitMargin { get; set; }

    /// <summary>Annual revenue in the studio's local currency; absent until audited.</summary>
    [Predicate("annualRevenue")]
    public decimal? AnnualRevenue { get; set; }

    /// <summary>Date the studio permanently closed; <see langword="null"/> while still active.</summary>
    [Predicate("closedOn")]
    public DateOnly? ClosedOn { get; set; }

    /// <summary>Timestamp when the studio was deactivated in the platform; <see langword="null"/> if still active.</summary>
    [Predicate("deactivatedAt")]
    public DateTimeOffset? DeactivatedAt { get; set; }

    /// <summary>IRI-safe legacy identifier carried over from a previous system; optional.</summary>
    [Predicate("legacyId")]
    public Guid? LegacyId { get; set; }

    /// <summary>Link to the studio's primary social-media profile; optional.</summary>
    [Predicate("socialLink")]
    public Uri? SocialLink { get; set; }

    // ── Owned relations ───────────────────────────────────────────────────────

    /// <summary>
    /// N:1 — the <see cref="Artist"/> who manages this studio.
    /// Many studios can be managed by the same artist. Stored as a single IRI
    /// (<c>studio:managedBy → artist-IRI</c>) in this entity's RDF graph.
    /// <c>null</c> when no managing artist has been assigned yet.
    /// </summary>
    [Owning("managedBy")]
    public partial EntityRef<Artist>? ManagedBy { get; set; }

    /// <summary>
    /// 1:N — the ordered list of <see cref="Recording"/> sessions produced in this studio.
    /// Stored as an <c>rdf:List</c> in this entity's graph; order is preserved.
    /// Each recording belongs conceptually to exactly one studio (one-to-many from the studio side).
    /// </summary>
    [Owning("hasRecording")]
    public partial EntityRefCollection<Recording> Recordings { get; }

    /// <summary>
    /// M:N — the music <see cref="Genre"/> labels associated with this studio.
    /// Many studios can share the same genre IRI (many-to-many, owning side on Studio).
    /// Stored as an ordered <c>rdf:List</c> of genre IRIs in this entity's graph.
    /// </summary>
    [Owning("hasGenre")]
    public partial EntityRefCollection<Genre> Genres { get; }
}
