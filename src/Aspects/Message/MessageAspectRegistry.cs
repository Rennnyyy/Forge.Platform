using System.Collections.Concurrent;

namespace Forge.Aspects.Message;

/// <summary>
/// Default <see cref="IMessageAspectRegistry"/> implementation.
/// Seals itself on first <see cref="TryGet"/> call — all registrations must complete
/// before any read.
/// </summary>
internal sealed class MessageAspectRegistry : IMessageAspectRegistry
{
    private readonly ConcurrentDictionary<(Type MessageType, MessageKind Kind), IMessageAspect>
        _registrations = new();

    private int _sealed; // 0 = open, 1 = sealed

    public void Register(IMessageAspect aspect, Type messageType, MessageKind kind)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(messageType);
        EnsureNotSealed();

        foreach (MessageKind bit in ExpandBits(kind))
        {
            var key = (messageType, bit);
            if (!_registrations.TryAdd(key, aspect))
                throw new InvalidOperationException(
                    $"An aspect for message type '{messageType.Name}' / {bit} is already registered.");
        }
    }

    public IMessageAspect? TryGet(Type messageType, MessageKind kind)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        Seal();
        _registrations.TryGetValue((messageType, kind), out var result);
        return result;
    }

    private void EnsureNotSealed()
    {
        if (Volatile.Read(ref _sealed) != 0)
            throw new InvalidOperationException(
                "Cannot register message aspects after the registry has been sealed. " +
                "All registrations must complete before the first TryGet call.");
    }

    private void Seal() => Interlocked.CompareExchange(ref _sealed, 1, 0);

    private static IEnumerable<MessageKind> ExpandBits(MessageKind kind)
    {
        foreach (MessageKind bit in Enum.GetValues<MessageKind>())
            if ((kind & bit) != 0)
                yield return bit;
    }
}
