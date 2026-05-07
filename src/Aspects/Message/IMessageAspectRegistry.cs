using Forge.Aspects.Abstractions;
namespace Forge.Aspects.Message;

/// <summary>
/// In-process registry of code-origin message aspects, keyed by CLR message type and
/// <see cref="MessageKind"/>. Seals itself on first <see cref="TryGet"/> call.
/// </summary>
public interface IMessageAspectRegistry
{
    /// <summary>
    /// Register <paramref name="aspect"/> for the given <paramref name="messageType"/> and
    /// <paramref name="kind"/>. Throws <see cref="InvalidOperationException"/> if the registry
    /// is sealed or an identical registration already exists.
    /// </summary>
    void Register(IMessageAspect aspect, Type messageType, MessageKind kind);

    /// <summary>
    /// Returns the <see cref="IMessageAspect"/> registered for <paramref name="messageType"/>
    /// and <paramref name="kind"/>, or <c>null</c> if no such registration exists.
    /// Seals the registry on first call.
    /// </summary>
    IMessageAspect? TryGet(Type messageType, MessageKind kind);
}
