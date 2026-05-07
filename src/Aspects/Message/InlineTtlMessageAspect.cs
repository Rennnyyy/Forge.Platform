using Forge.Aspects.Abstractions;
namespace Forge.Aspects.Message;

/// <summary>
/// A <see cref="IMessageAspect"/> that holds shape data as an inline Turtle string.
/// Used for code-origin message aspects in tests and DI registrations.
/// See Capability ADR-0001.
/// </summary>
public sealed class InlineTtlMessageAspect : IMessageAspect
{
    /// <inheritdoc/>
    public string Iri { get; }

    /// <inheritdoc/>
    public string? ShapeTtl { get; }

    /// <param name="iri">Canonical IRI identifying this aspect. Must not be <see cref="Aspect.NoOpIri"/>.</param>
    /// <param name="shapeTtl">
    /// Turtle-serialized SHACL shape validated against the projected message graph,
    /// or <c>null</c> if no shape check is required.
    /// </param>
    public InlineTtlMessageAspect(string iri, string? shapeTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iri);
        if (iri == Aspect.NoOpIri)
            throw new ArgumentException("The NoOp IRI is reserved.", nameof(iri));

        Iri = iri;
        ShapeTtl = shapeTtl;
    }
}
