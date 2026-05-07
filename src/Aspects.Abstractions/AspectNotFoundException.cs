namespace Forge.Aspects.Abstractions;

/// <summary>
/// Thrown when an aspect IRI cannot be resolved from <see cref="IAspectStore"/>.
/// </summary>
public sealed class AspectNotFoundException : Exception
{
    /// <summary>The IRI that was not found in the store.</summary>
    public string AspectIri { get; }

    public AspectNotFoundException(string aspectIri)
        : base($"No aspect registered for IRI '{aspectIri}'.")
    {
        AspectIri = aspectIri;
    }

    public AspectNotFoundException(string aspectIri, string context)
        : base($"No aspect registered for IRI '{aspectIri}' ({context}).")
    {
        AspectIri = aspectIri;
    }
}
