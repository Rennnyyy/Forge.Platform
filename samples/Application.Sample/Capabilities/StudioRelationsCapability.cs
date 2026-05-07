using Forge.Aspects;
using Forge.Aspects.Abstractions;
using Forge.Capability;
using Forge.Entity;
using Forge.Operations;
using Forge.Repository;
using Forge.Repository.Transaction;

namespace Forge.Application.Sample;

// ── Command / Response ─────────────────────────────────────────────────────────

/// <summary>
/// Command for <see cref="CreateLinkedStudioHandler"/>.
/// Only <see cref="Name"/> and <see cref="FoundedYear"/> are required scalar fields;
/// all relation IRIs are optional so that individual requests can exercise each
/// flavour independently.
/// </summary>
public sealed record CreateLinkedStudioCommand(
    /// <summary>Display name of the studio.</summary>
    string Name,
    /// <summary>Calendar year the studio opened (e.g. 2001).</summary>
    int FoundedYear,
    /// <summary>
    /// IRI of the <see cref="Artist"/> who manages this studio.
    /// Demonstrates the N:1 <see cref="EntityRef{T}"/> flavour.
    /// Returns <c>422 RELATION_NOT_FOUND</c> if the IRI does not exist in the store.
    /// </summary>
    string? ManagedByArtistIri = null,
    /// <summary>
    /// Ordered IRIs of <see cref="Recording"/> entities produced in this studio.
    /// Demonstrates the 1:N <see cref="EntityRefCollection{T}"/> flavour.
    /// Returns <c>422 RELATION_NOT_FOUND</c> for any IRI that does not exist in the store.
    /// </summary>
    IReadOnlyList<string>? RecordingIris = null,
    /// <summary>
    /// IRIs of <see cref="Genre"/> named-individuals associated with this studio.
    /// Demonstrates the M:N <see cref="EntityRefCollection{T}"/> flavour with an
    /// <see cref="EnumerationAttribute"/> target type (validated against <see cref="Genre.All"/>).
    /// Returns <c>422 RELATION_NOT_FOUND</c> for any IRI that is not a known genre IRI.
    /// </summary>
    IReadOnlyList<string>? GenreIris = null);

/// <summary>
/// Response from <see cref="CreateLinkedStudioHandler"/>.
/// Carries the newly created studio IRI and all linked relation IRIs so Bruno assertions
/// can verify each relation flavour independently.
/// </summary>
public sealed record CreateLinkedStudioResponse(
    string StudioIri,
    string? ManagedByArtistIri,
    IReadOnlyList<string> RecordingIris,
    IReadOnlyList<string> GenreIris);

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Demonstrates wiring all three owned-relation flavours on <see cref="Studio"/>
/// and returning <c>422 RELATION_NOT_FOUND</c> when any supplied IRI does not exist.
///
/// <list type="table">
///   <listheader><term>Relation</term><description>Validation mechanism</description></listheader>
///   <item>
///     <term><see cref="Studio.ManagedBy"/> (N:1 <see cref="EntityRef{T}"/>)</term>
///     <description>
///       <see cref="EntityOperations.ReadAsync{T}(string, System.Threading.CancellationToken)"/>
///       against <see cref="Artist"/> — null → 422.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Studio.Recordings"/> (1:N <see cref="EntityRefCollection{T}"/>)</term>
///     <description>
///       <see cref="EntityOperations.ReadAsync{T}(string, System.Threading.CancellationToken)"/>
///       against <see cref="Recording"/> per IRI — null → 422.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Studio.Genres"/> (M:N <see cref="EntityRefCollection{T}"/>)</term>
///     <description>
///       <see cref="Genre.All"/> look-up (enumeration type; not stored in the repository)
///       — no match → 422.
///     </description>
///   </item>
/// </list>
///
/// See <a href="../adr/0007-wrong-iri-error-demonstration.md">sample ADR-0007</a>.
/// </summary>
[Capability("demo.studio.create-linked")]
public sealed class CreateLinkedStudioHandler
    : ICapabilityHandler<CreateLinkedStudioCommand, CreateLinkedStudioResponse>
{
    private readonly ITransactionalEntityStore _store;

    public CreateLinkedStudioHandler(ITransactionalEntityStore store) => _store = store;

    public async ValueTask<CapabilityResult<CreateLinkedStudioResponse>> HandleAsync(
        CreateLinkedStudioCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default)
    {
        using var _ = EntityOperations.Use(_store);

        // ── Pass 1: validate N:1 EntityRef ────────────────────────────────────
        Artist? managedByArtist = null;
        if (command.ManagedByArtistIri is not null)
        {
            managedByArtist = await EntityOperations.ReadAsync<Artist>(
                command.ManagedByArtistIri, cancellationToken);

            if (managedByArtist is null)
                return new CapabilityResult<CreateLinkedStudioResponse>.Fail(
                    new CapabilityError("RELATION_NOT_FOUND",
                        $"Artist '{command.ManagedByArtistIri}' does not exist."));
        }

        // ── Pass 2: validate 1:N EntityRefCollection ──────────────────────────
        var recordings = new List<Recording>();
        foreach (var iri in command.RecordingIris ?? [])
        {
            var recording = await EntityOperations.ReadAsync<Recording>(iri, cancellationToken);
            if (recording is null)
                return new CapabilityResult<CreateLinkedStudioResponse>.Fail(
                    new CapabilityError("RELATION_NOT_FOUND",
                        $"Recording '{iri}' does not exist."));

            recordings.Add(recording);
        }

        // ── Pass 3: validate M:N EntityRefCollection (Enumeration target) ─────
        var genres = new List<Genre>();
        foreach (var iri in command.GenreIris ?? [])
        {
            var genre = Genre.All.FirstOrDefault(g => g.Iri == iri);
            if (genre is null)
                return new CapabilityResult<CreateLinkedStudioResponse>.Fail(
                    new CapabilityError("RELATION_NOT_FOUND",
                        $"Genre '{iri}' is not a known genre IRI."));

            genres.Add(genre);
        }

        // ── Build entity ──────────────────────────────────────────────────────
        var studio = new Studio
        {
            Name = command.Name,
            Active = true,
            FoundedYear = command.FoundedYear,
            SessionCount = 0,
            AcousticRating = 0f,
            ReputationScore = 0.0,
            Budget = 0m,
            OpenedOn = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            LastBookedAt = DateTimeOffset.UtcNow,
            ExternalId = Guid.NewGuid(),
            Website = new Uri("https://forge-it.net"),
        };

        if (managedByArtist is not null)
            studio.ManagedBy = EntityRef<Artist>.ForIri(managedByArtist.Iri);

        foreach (var rec in recordings)
            await studio.Recordings.AddAsync(rec, cancellationToken);

        foreach (var genre in genres)
            await studio.Genres.AddAsync(genre, cancellationToken);

        // ── Persist ───────────────────────────────────────────────────────────
        await using var tx = EntityOperations.BeginTransaction();
        tx.Create(studio, Aspect.NoOpIri);
        await tx.CommitAsync(cancellationToken);

        return new CapabilityResult<CreateLinkedStudioResponse>.Ok(
            new CreateLinkedStudioResponse(
                studio.Iri,
                managedByArtist?.Iri,
                recordings.Select(r => r.Iri).ToList(),
                genres.Select(g => g.Iri).ToList()));
    }
}
