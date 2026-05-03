using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// An <see cref="Forge.Repository.IAspect"/> that carries SHACL shape material for
/// message validation (commands, responses, events).
/// The engine casts <c>IAspect</c> to this interface to obtain shape data.
/// See Capability ADR-0001.
/// </summary>
public interface IMessageAspect : Forge.Repository.IAspect
{
    /// <summary>
    /// Turtle-serialized SHACL shape validated against the projected message graph,
    /// or <c>null</c> if no shape check is required.
    /// </summary>
    string? ShapeTtl { get; }
}
