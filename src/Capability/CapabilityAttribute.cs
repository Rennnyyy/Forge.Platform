namespace Forge.Capability;

/// <summary>
/// Marks an <see cref="ICapabilityHandler{TCommand,TResponse}"/> implementation with a
/// stable, transport-agnostic identity string.
/// <para>
/// The identity is a dot-separated name (e.g. <c>"catalog.artists.create"</c>).
/// The HTTP transport converts it to a route path via
/// <see cref="CapabilityIdentity.ToRoutePath"/>; future transports apply their own
/// separator conventions.
/// </para>
/// See Capability ADR-0010.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CapabilityAttribute : Attribute
{
    /// <summary>
    /// Initialises the attribute with the given dot-separated identity string.
    /// </summary>
    /// <param name="identity">
    /// A dot-separated capability identity; each segment must satisfy
    /// <c>^[a-z0-9]([a-z0-9-]*[a-z0-9])?$</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="identity"/> contains an invalid segment.
    /// </exception>
    public CapabilityAttribute(string identity)
    {
        Identity = new CapabilityIdentity(identity);
    }

    /// <summary>The validated capability identity.</summary>
    public CapabilityIdentity Identity { get; }
}
