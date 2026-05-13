using Confluent.Kafka;
using Forge.Messaging.Abstractions;

namespace Forge.Messaging.Kafka;

/// <summary>
/// Kafka-backed implementation of <see cref="IMessageProducer{TKey,TValue}"/>.
/// Serializes <see cref="MessageEnvelope{TValue}"/> to JSON bytes and produces to the
/// topic specified by <see cref="MessageEnvelope{TValue}.Topic"/> using the envelope's
/// <see cref="MessageEnvelope{TValue}.PartitionKey"/> as the Kafka message key.
/// <para>
/// The underlying <see cref="IProducer{TKey,TValue}"/> is shared across all
/// <typeparamref name="TValue"/> registrations through the singleton
/// <see cref="KafkaProducerHandle"/>.
/// </para>
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TKey">Partition key type. Always <see cref="string"/> in platform use.</typeparam>
/// <typeparam name="TValue">Payload type.</typeparam>
internal sealed class KafkaMessageProducer<TKey, TValue> : IMessageProducer<TKey, TValue>, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly IMessageSerializer<MessageEnvelope<TValue>> _serializer;

    public KafkaMessageProducer(
        KafkaProducerHandle handle,
        IMessageSerializer<MessageEnvelope<TValue>> serializer)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(serializer);
        _producer = handle.Producer;
        _serializer = serializer;
    }

    /// <inheritdoc/>
    public async ValueTask ProduceAsync(
        MessageEnvelope<TValue> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var bytes = _serializer.Serialize(envelope);
        var msg = new Message<string, byte[]>
        {
            Key = envelope.PartitionKey,
            Value = bytes.ToArray(),
        };

        await _producer.ProduceAsync(envelope.Topic, msg, cancellationToken)
                       .ConfigureAwait(false);
    }

    public void Dispose() { /* producer lifetime owned by KafkaProducerHandle */ }
}
