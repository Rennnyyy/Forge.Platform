using System.Collections.Concurrent;
using System.Threading.Channels;
using Forge.Messaging.Abstractions;

namespace Forge.Messaging.InMemory;

/// <summary>
/// In-process message broker backed by <see cref="System.Threading.Channels"/>.
/// Maintains one unbounded channel per <c>(topic, payload type)</c> pair.
/// <para>
/// Intended for unit tests and sample applications. No Kafka broker required.
/// Call <see cref="Reset"/> between test runs to discard all pending messages
/// and complete any active consumer enumerations.
/// </para>
/// See root ADR-0020.
/// </summary>
public sealed class InMemoryMessageBroker
{
    private readonly ConcurrentDictionary<string, (object Channel, Action Complete)> _channels = new();

    /// <summary>
    /// Returns the channel for the given <paramref name="topic"/> and payload type
    /// <typeparamref name="TValue"/>, creating it on first access.
    /// </summary>
    internal Channel<MessageEnvelope<TValue>> GetOrCreate<TValue>(string topic)
    {
        var key = FormattableString.Invariant($"{topic}::{typeof(TValue)}");
        var entry = _channels.GetOrAdd(key, _ =>
        {
            var ch = Channel.CreateUnbounded<MessageEnvelope<TValue>>();
            return (ch, () => ch.Writer.TryComplete());
        });
        return (Channel<MessageEnvelope<TValue>>)entry.Channel;
    }

    /// <summary>
    /// Completes all open channel writers and clears all topics.
    /// Active <see cref="IMessageConsumer{TKey,TValue}.ConsumeAsync"/> enumerations
    /// will complete normally after the last buffered message is consumed.
    /// </summary>
    public void Reset()
    {
        foreach (var (_, complete) in _channels.Values)
            complete();

        _channels.Clear();
    }
}
