using Forge.Capability;
using Forge.Entity;

namespace Forge.Application.Sample;

/// <summary>
/// Sample entity used to demonstrate the full Forge platform stack with
/// <strong>every supported scalar CLR type</strong> and its nullable variant:
/// <c>string</c>, <c>bool</c>, <c>int</c>, <c>long</c>, <c>float</c>,
/// <c>double</c>, <c>decimal</c>, <c>DateOnly</c>, <c>DateTimeOffset</c>,
/// <c>Guid</c>, <c>Uri</c> — non-nullable and nullable.
/// <br/>
/// Uses <c>[CrudCapabilities]</c> to auto-generate five
/// <c>ICapabilityHandler</c> implementations (Create, Read, Update, Delete,
/// List) wired to HTTP endpoints under <c>/data-records/</c> by
/// <c>MapCapabilities()</c>.
/// </summary>
[Entity(Path = "data-records")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
[CrudCapabilities]
public partial class DataRecord
{
    /// <summary>
    /// Primary identifier — a short URL-safe key string.
    /// Forms the IRI suffix: <c>{baseIri}/data-records/{key}</c>.
    /// </summary>
    [IdentityPart(0)]
    [Predicate("key")]
    public partial string Key { get; init; }

    // ── Non-nullable scalars ─────────────────────────────────────────────────

    [Predicate("label")]
    public string Label { get; set; } = string.Empty;

    [Predicate("active")]
    public bool Active { get; set; }

    [Predicate("count")]
    public int Count { get; set; }

    [Predicate("serial")]
    public long Serial { get; set; }

    [Predicate("ratio")]
    public float Ratio { get; set; }

    [Predicate("score")]
    public double Score { get; set; }

    [Predicate("amount")]
    public decimal Amount { get; set; }

    [Predicate("effectiveDate")]
    public DateOnly EffectiveDate { get; set; }

    [Predicate("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Predicate("correlationId")]
    public Guid CorrelationId { get; set; }

    [Predicate("reference")]
    public Uri Reference { get; set; } = new Uri("https://forge-it.net/");

    // ── Nullable variants ────────────────────────────────────────────────────

    [Predicate("notes")]
    public string? Notes { get; set; }

    [Predicate("flagged")]
    public bool? Flagged { get; set; }

    [Predicate("limit")]
    public int? Limit { get; set; }

    [Predicate("sequence")]
    public long? Sequence { get; set; }

    [Predicate("factor")]
    public float? Factor { get; set; }

    [Predicate("precision")]
    public double? Precision { get; set; }

    [Predicate("fee")]
    public decimal? Fee { get; set; }

    [Predicate("expiresOn")]
    public DateOnly? ExpiresOn { get; set; }

    [Predicate("archivedAt")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [Predicate("alternateId")]
    public Guid? AlternateId { get; set; }

    [Predicate("canonicalUri")]
    public Uri? CanonicalUri { get; set; }
}
