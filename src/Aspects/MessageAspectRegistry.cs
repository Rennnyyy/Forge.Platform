using System.Collections.Concurrent;

namespace Forge.Aspects;

/// <summary>
/// Default implementation of <see cref="IMessageAspectRegistry"/>.
/// All <see cref="Register"/> calls must complete before the first <see cref="TryGet"/> call.
/// After the first <see cref="TryGet"/> the registry is sealed; subsequent <see cref="Register"/>
/// calls throw <see cref="InvalidOperationException"/>. Reads are lock-free.
/// </summary>
internal sealed class MessageAspectRegistry : IMessageAspectRegistry
{
    private readonly ConcurrentDictionary<(Type MessageType, MessageKind Kind), IMessageAspect>
        _registrations = new();

    // 0 = open, 1 = sealed (set on first TryGet)
    private int _sealed;

    public IMessageAspect? TryGet(Type messageType, MessageKind kind)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        System.Threading.Interlocked.CompareExchange(ref _sealed, 1, 0);

        _registrations.TryGetValue((messageType, kind), out var result);
        return result;
    }

    public void Register(IMessageAspect aspect, Type messageType, MessageKind kind)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentNullException.ThrowIfNull(messageType);

        if (_sealed != 0)
            throw new InvalidOperationException(
                "Cannot register aspects after the registry has been read (registry is sealed after first TryGet call).");

        foreach (MessageKind bit in ExpandBits(kind))
        {
            var key = (messageType, bit);
            if (!_registrations.TryAdd(key, aspect))
                throw new InvalidOperationException(
                    $"An aspect is already registered for '{messageType.Name}' / {bit}.");
        }
    }

    private static IEnumerable<MessageKind> ExpandBits(MessageKind kind)
    {
        foreach (MessageKind bit in Enum.GetValues<MessageKind>())
            if ((kind & bit) != 0)
                yield return bit;
    }
}
