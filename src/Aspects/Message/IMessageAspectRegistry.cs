namespace Forge.Aspects.Message;

/// <summary>
/// Runtime registry that maps <c>(CLR Type, <see cref="MessageKind"/>)</c> to an
/// <see cref="IMessageAspect"/>. Returns <c>null</c> on a cache miss — never throws.
/// This is the explicit permissive policy that distinguishes the message leg from the
/// write leg (<see cref="IAspectResolver.Resolve"/> throws on miss).
/// See Capability ADR-0001.
/// </summary>
public interface IMessageAspectRegistry
{
    /// <summary>
    /// Returns the registered aspect for (<paramref name="messageType"/>, <paramref name="kind"/>),
    /// or <c>null</c> if none is registered. Never throws for a missing registration.
    /// </summary>
    IMessageAspect? TryGet(Type messageType, MessageKind kind);

    /// <summary>
    /// Registers <paramref name="aspect"/> for (<paramref name="messageType"/>, <paramref name="kind"/>).
    /// Throws <see cref="InvalidOperationException"/> if an aspect is already registered for the
    /// same key, or if <see cref="TryGet"/> has already been called (registry is sealed after first read).
    /// <para>
    /// <paramref name="kind"/> may be a combined <c>[Flags]</c> value; each bit is stored as a
    /// separate entry.
    /// </para>
    /// </summary>
    void Register(IMessageAspect aspect, Type messageType, MessageKind kind);
}
