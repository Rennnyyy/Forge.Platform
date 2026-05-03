namespace Forge.Aspects;

/// <summary>Categorises the role of a capability message.</summary>
[Flags]
public enum MessageKind
{
    /// <summary>An inbound command sent to a capability handler.</summary>
    Command = 1,

    /// <summary>An outbound response returned by a capability handler.</summary>
    Response = 2,

    /// <summary>A domain event published by a capability handler.</summary>
    Event = 4,
}
