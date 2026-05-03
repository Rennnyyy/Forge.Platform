using System.Collections.Immutable;

namespace Forge.Aspects;

/// <summary>
/// An <see cref="IAspect"/> that bundles message-validation aspect IRIs for a single
/// capability dispatch call. Register in <see cref="IAspectStore"/> by its own IRI so
/// the caller can govern an entire policy suite — command, response, and per-event-type
/// validation — with one IRI. See Capability ADR-0011.
/// </summary>
public sealed record CapabilityAspect : IAspect
{
    /// <summary>The canonical IRI identifying this capability aspect bundle.</summary>
    public required string Iri { get; init; }

    /// <summary>
    /// IRI of the <see cref="IMessageAspect"/> applied to the incoming command, or
    /// <c>null</c> for permissive (no validation).
    /// </summary>
    public string? CommandAspectIri { get; init; }

    /// <summary>
    /// IRI of the <see cref="IMessageAspect"/> applied to the outgoing response, or
    /// <c>null</c> for permissive.
    /// </summary>
    public string? ResponseAspectIri { get; init; }

    /// <summary>
    /// Map from event CLR type to the IRI of the <see cref="IMessageAspect"/> applied
    /// to that event. A missing key is treated as permissive for that event type.
    /// </summary>
    public IReadOnlyDictionary<Type, string> EventAspectIris { get; init; }
        = ImmutableDictionary<Type, string>.Empty;
}
