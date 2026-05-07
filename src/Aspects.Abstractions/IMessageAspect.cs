namespace Forge.Aspects.Abstractions;

/// <summary>
/// An <see cref="IAspect"/> that validates capability message objects (commands, responses,
/// events) against a SHACL shape. Resolved from <see cref="IAspectStore"/> by the message
/// aspect engine. See Capability ADR-0001.
/// </summary>
public interface IMessageAspect : IAspect
{
    /// <summary>
    /// Turtle-serialized SHACL shape validated against the projected message graph, or
    /// <c>null</c> if no shape check is required.
    /// </summary>
    string? ShapeTtl { get; }
}
