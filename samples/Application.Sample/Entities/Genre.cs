using Forge.Entity;
using Forge.Operations;

namespace Forge.Application.Sample;

/// <summary>
/// Bounded music-genre vocabulary declared as a set of RDF named individuals.
/// <br/>
/// <c>[Enumeration]</c> instructs the generator to emit a static constructor that calls
/// <c>MaterializeIdentity()</c> on every <c>public static readonly</c> field, sealing each
/// instance's IRI at class-load time — no database writes are needed.
/// <c>[OperationEndpoints]</c> is present so <c>MapOperations()</c> auto-discovers Genre;
/// because the class also carries <c>[Enumeration]</c>, only the read-only
/// <c>GET api/entities/genres</c> endpoints (List + Read by IRI) are registered—
/// Create, Update, and Delete are suppressed automatically. See sample ADR-0006.
/// <br/>
/// <b>IRI pattern</b>: <c>https://forge-it.net/genres/{slug}</c><br/>
/// <b>HTTP surface</b>: GET only (list + single by IRI). No Create, Update, or Delete.
/// </summary>
[Entity(Path = "genres", PredicatePath = "genre")]
[Identity(IdentityGenerator.PropertyBasedPlain)]
[Enumeration]
[OperationEndpoints]
public sealed partial class Genre
{
    // ── Named individuals ─────────────────────────────────────────────────────
    // The generator emits a static ctor that calls MaterializeIdentity() on each
    // field below, using EntityOptions.Current.BaseIri (default "https://forge-it.net").

    /// <summary>Jazz — a genre from New Orleans blending blues, ragtime, and improvisation.</summary>
    public static readonly Genre Jazz = new() { Slug = "jazz", Name = "Jazz", Description = "A genre born in New Orleans combining blues, ragtime, and improvisation." };

    /// <summary>Classical — the Western art-music tradition from roughly 1750–1820.</summary>
    public static readonly Genre Classical = new() { Slug = "classical", Name = "Classical", Description = "Western art music rooted in formal composition and orchestral performance." };

    /// <summary>Electronic — music produced primarily from electronic instruments and synthesis.</summary>
    public static readonly Genre Electronic = new() { Slug = "electronic", Name = "Electronic", Description = "Music produced primarily from electronic synthesis and digital production." };

    /// <summary>Ambient — atmospheric, texture-driven music designed for passive listening.</summary>
    public static readonly Genre Ambient = new() { Slug = "ambient", Name = "Ambient", Description = "Atmospheric and texture-driven music intended for passive or background listening." };

    /// <summary>Pop — melodic, commercially oriented popular music.</summary>
    public static readonly Genre Pop = new() { Slug = "pop", Name = "Pop", Description = "Melodic, hook-driven popular music oriented toward wide commercial appeal." };

    // ── All — ordered list for endpoint reflection ─────────────────────────────
    // Populated after the static ctor has called MaterializeIdentity() on each instance,
    // so every member already has its IRI sealed when this property is accessed.

    /// <summary>All five named genre individuals in declaration order.</summary>
    public static IReadOnlyList<Genre> All { get; } = [Jazz, Classical, Electronic, Ambient, Pop];

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>
    /// URL-safe slug that forms the IRI suffix: <c>{baseIri}/genres/{slug}</c>.
    /// Examples: "jazz", "classical", "electronic", "ambient", "pop".
    /// </summary>
    [IdentityPart(0)]
    [Predicate("slug")]
    public partial string Slug { get; init; }

    /// <summary>Human-readable display name (e.g. "Jazz", "Classical").</summary>
    [Predicate("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description of the genre.</summary>
    [Predicate("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Inverse — the <see cref="Studio"/> entities that list this genre in their
    /// <see cref="Studio.Genres"/> collection. Populated at load time by the inverse
    /// collection loader (ADR-0018). Read-only; mutate via <see cref="Studio.Genres"/>.
    /// <br/>
    /// <c>Lazy = true</c> — skipped in list responses (key omitted); hydrated on single read.
    /// </summary>
    [Inverse(nameof(Studio.Genres), "hasGenre", Lazy = true)]
    public partial EntityRefCollection<Studio> ProducedBy { get; }
}

