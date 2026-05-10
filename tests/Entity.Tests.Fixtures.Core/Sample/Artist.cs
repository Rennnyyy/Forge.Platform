using Forge.Entity;

namespace Forge.Entity.Tests.Fixtures.Sample;

/// <summary>
/// UuidV5 (PropertyBasedEncoded) entity. Two identity parts: Name + Country.
/// Carries every supported scalar CLR type to exercise the full LiteralCodec
/// surface across InMemory and GraphDB backends.
/// </summary>
[Entity(Path = "artists", PredicatePath = "artist")]
[Identity(IdentityGenerator.PropertyBasedEncoded)]
public partial class Artist
{
    [IdentityPart(0)]
    [Predicate("name")]
    public partial string Name { get; init; }

    [IdentityPart(1)]
    [Predicate("country")]
    public partial string Country { get; init; }

    /// <summary>Nullable string — intentionally omitted on some artists.</summary>
    [Predicate("bio")]
    public string? Bio { get; set; }

    [Predicate("active")]
    public bool Active { get; set; } = true;

    [Predicate("debutYear")]
    public int DebutYear { get; set; }

    [Predicate("streamCount")]
    public long StreamCount { get; set; }

    [Predicate("avgBpm")]
    public float AvgBpm { get; set; }

    [Predicate("popularity")]
    public double Popularity { get; set; }

    [Predicate("totalEarnings")]
    public decimal TotalEarnings { get; set; }

    [Predicate("bornOn")]
    public DateOnly BornOn { get; set; }

    [Predicate("registeredAt")]
    public DateTimeOffset RegisteredAt { get; set; }

    [Predicate("externalId")]
    public Guid ExternalId { get; set; }

    /// <summary>Nullable Uri — intentionally omitted on some artists.</summary>
    [Predicate("website")]
    public Uri? Website { get; set; }

    /// <summary>
    /// M:N inverse — Albums that list this artist as a performer
    /// (inverse of <see cref="Album.Artists"/>).  Read-only; mutate via
    /// <see cref="Album.Artists"/>.
    /// </summary>
    [Inverse(nameof(Album.Artists), "hasArtist")]
    public partial EntityRefCollection<Album> Albums { get; }
}
