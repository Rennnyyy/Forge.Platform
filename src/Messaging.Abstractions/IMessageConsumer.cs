namespace Forge.Messaging.Abstractions;

/// <summary>
/// Reads messages from a named topic as an async stream.
/// <para>
/// The application owns the consumer loop: inject an <see cref="IMessageConsumer{TKey,TValue}"/>,
/// call <see cref="ConsumeAsync"/> inside a hosted service, and iterate until the
/// <paramref name="cancellationToken"/> is cancelled.
/// </para>
/// <para>
/// <typeparamref name="TKey"/> is the logical partition key type.
/// All platform uses fix <typeparamref name="TKey"/> to <see cref="string"/>.
/// </para>
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TKey">Partition key type. Always <see cref="string"/> in platform use.</typeparam>
/// <typeparam name="TValue">Payload type.</typeparam>
public interface IMessageConsumer<TKey, TValue>
{
    /// <summary>
    /// Returns an async stream of all envelopes arriving on <paramref name="topic"/>.
    /// Completes when <paramref name="cancellationToken"/> is cancelled, the topic
    /// is closed by the broker, or the underlying channel is completed (InMemory).
    /// </summary>
    IAsyncEnumerable<MessageEnvelope<TValue>> ConsumeAsync(
        string topic,
        CancellationToken cancellationToken = default);
}
