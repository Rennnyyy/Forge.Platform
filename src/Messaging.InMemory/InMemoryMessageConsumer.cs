using System.Runtime.CompilerServices;
using Forge.Messaging.Abstractions;

namespace Forge.Messaging.InMemory;

/// <summary>
/// Reads <see cref="MessageEnvelope{TValue}"/> instances from an
/// <see cref="InMemoryMessageBroker"/> channel as an async stream.
/// No deserialization occurs; the payload is returned by reference.
/// <para>
/// The enumeration completes when the <paramref name="cancellationToken"/> is cancelled
/// or the channel is completed via <see cref="InMemoryMessageBroker.Reset"/>.
/// </para>
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TKey">Partition key type (unused in InMemory; present for DI compatibility).</typeparam>
/// <typeparam name="TValue">Payload type.</typeparam>
public sealed class InMemoryMessageConsumer<TKey, TValue> : IMessageConsumer<TKey, TValue>
{
    private readonly InMemoryMessageBroker _broker;

    /// <summary>Initializes the consumer with the shared <paramref name="broker"/>.</summary>
    public InMemoryMessageConsumer(InMemoryMessageBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MessageEnvelope<TValue>> ConsumeAsync(
        string topic,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var channel = _broker.GetOrCreate<TValue>(topic);
        await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return envelope;
    }
}
