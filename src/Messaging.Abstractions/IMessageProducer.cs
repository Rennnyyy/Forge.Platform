namespace Forge.Messaging.Abstractions;

/// <summary>
/// Publishes a single message to a named topic.
/// <para>
/// <typeparamref name="TKey"/> is the logical partition key type.
/// All platform uses fix <typeparamref name="TKey"/> to <see cref="string"/>; the type
/// parameter exists for DI-level type discrimination and Kafka implementation typing.
/// </para>
/// <para>
/// All routing and metadata information is carried inside <paramref name="envelope"/>;
/// callers do not duplicate the topic or key outside the envelope.
/// </para>
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TKey">Partition key type. Always <see cref="string"/> in platform use.</typeparam>
/// <typeparam name="TValue">Payload type.</typeparam>
public interface IMessageProducer<TKey, TValue>
{
    /// <summary>
    /// Publishes <paramref name="envelope"/> to the broker topic specified by
    /// <see cref="MessageEnvelope{TValue}.Topic"/>.
    /// </summary>
    ValueTask ProduceAsync(
        MessageEnvelope<TValue> envelope,
        CancellationToken cancellationToken = default);
}
