using Forge.Messaging.Abstractions;

namespace Forge.Messaging.InMemory;

/// <summary>
/// Writes <see cref="MessageEnvelope{TValue}"/> instances into an
/// <see cref="InMemoryMessageBroker"/> channel. No serialization occurs;
/// the payload is passed by reference.
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TKey">Partition key type (unused in InMemory; present for DI compatibility).</typeparam>
/// <typeparam name="TValue">Payload type.</typeparam>
public sealed class InMemoryMessageProducer<TKey, TValue> : IMessageProducer<TKey, TValue>
{
    private readonly InMemoryMessageBroker _broker;

    /// <summary>Initializes the producer with the shared <paramref name="broker"/>.</summary>
    public InMemoryMessageProducer(InMemoryMessageBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        _broker = broker;
    }

    /// <inheritdoc/>
    public ValueTask ProduceAsync(
        MessageEnvelope<TValue> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        _broker.GetOrCreate<TValue>(envelope.Topic).Writer.TryWrite(envelope);
        return ValueTask.CompletedTask;
    }
}
