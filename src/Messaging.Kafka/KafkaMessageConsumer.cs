using System.Runtime.CompilerServices;
using Confluent.Kafka;
using Forge.Messaging.Abstractions;

namespace Forge.Messaging.Kafka;

/// <summary>
/// Kafka-backed implementation of <see cref="IMessageConsumer{TKey,TValue}"/>.
/// Each call to <see cref="ConsumeAsync"/> builds a dedicate Confluent consumer,
/// subscribes to <paramref name="topic"/>, and yields deserialized
/// <see cref="MessageEnvelope{TValue}"/> messages until the
/// <paramref name="cancellationToken"/> is cancelled.
/// <para>
/// Offsets are committed manually after each message is yielded to the caller.
/// This provides at-least-once delivery: if the caller crashes before processing,
/// the message is redelivered on the next poll.
/// </para>
/// <para>
/// Because each call builds its own consumer, multiple concurrent
/// <see cref="ConsumeAsync"/> calls against the same topic/group form a
/// standard Kafka consumer group. The application is responsible for
/// fan-out topologies (e.g. unique group IDs per replica).
/// </para>
/// See root ADR-0020.
/// </summary>
/// <typeparam name="TKey">Partition key type. Always <see cref="string"/> in platform use.</typeparam>
/// <typeparam name="TValue">Payload type.</typeparam>
internal sealed class KafkaMessageConsumer<TKey, TValue> : IMessageConsumer<TKey, TValue>
{
    private readonly KafkaConsumerOptions _options;
    private readonly IMessageDeserializer<MessageEnvelope<TValue>> _deserializer;

    public KafkaMessageConsumer(
        KafkaConsumerOptions options,
        IMessageDeserializer<MessageEnvelope<TValue>> deserializer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deserializer);
        _options = options;
        _deserializer = deserializer;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MessageEnvelope<TValue>> ConsumeAsync(
        string topic,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        using var consumer = new ConsumerBuilder<string, byte[]>(_options.ToConfluentConfig())
            .SetErrorHandler((_, e) =>
            {
                // Surface fatal errors; non-fatal ones are logged by Confluent internally.
                if (e.IsFatal)
                    throw new KafkaException(e);
            })
            .Build();

        consumer.Subscribe(topic);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? result;
                try
                {
                    // Confluent.Kafka Consume is synchronous; yield to the thread pool briefly
                    // so we don't block the async state machine on the hot path.
                    result = await Task.Run(
                        () => consumer.Consume(_options.PollTimeout),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (result is null || result.IsPartitionEOF)
                    continue;

                MessageEnvelope<TValue> envelope;
                try
                {
                    envelope = _deserializer.Deserialize(result.Message.Value);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize Kafka message from topic '{topic}': {ex.Message}", ex);
                }

                yield return envelope;

                if (!_options.EnableAutoCommit)
                    consumer.Commit(result);
            }
        }
        finally
        {
            consumer.Unsubscribe();
            consumer.Close();
        }
    }
}
