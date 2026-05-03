using Forge.Entity;
namespace Forge.Aspects;

/// <summary>
/// A <see cref="IMessageAspect"/> that holds shape data as an inline Turtle string.
/// Used for code-origin message aspects in tests and DI registrations.
/// See Capability ADR-0001.
/// </summary>
public sealed class InlineTtlMessageAspect : IMessageAspect
{
    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string? ShapeTtl { get; }

    /// <param name="name">Unique aspect name. Must not be <c>"noop"</c>.</param>
    /// <param name="shapeTtl">
    /// Turtle-serialized SHACL shape validated against the projected message graph,
    /// or <c>null</c> if no shape check is required.
    /// </param>
    public InlineTtlMessageAspect(string name, string? shapeTtl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name == "noop")
            throw new ArgumentException("The aspect name 'noop' is reserved.", nameof(name));

        Name = name;
        ShapeTtl = shapeTtl;
    }
}
