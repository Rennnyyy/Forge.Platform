using Confluent.Kafka;

namespace Forge.Messaging.Kafka;

/// <summary>
/// Configuration for the Kafka consumer.
/// Consumed by <see cref="KafkaMessageConsumer{TKey,TValue}"/>.
/// See root ADR-0020.
/// </summary>
public sealed class KafkaConsumerOptions
{
    /// <summary>
    /// Comma-separated list of Kafka bootstrap servers.
    /// Example: <c>localhost:9092</c> or <c>broker1:9092,broker2:9092</c>.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// Consumer group identifier. All consumers sharing the same group ID read
    /// from the topic as a competing-consumer group (each message delivered once
    /// per group). Use a unique group ID for broadcast/fan-out.
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Where to start reading when no committed offset is found for the consumer group.
    /// Defaults to <see cref="AutoOffsetReset.Latest"/> (only new messages).
    /// </summary>
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Latest;

    /// <summary>
    /// Whether to automatically commit offsets. Defaults to <c>false</c> to give
    /// the application explicit control over at-least-once processing.
    /// </summary>
    public bool EnableAutoCommit { get; set; } = false;

    /// <summary>
    /// Poll timeout used inside <see cref="KafkaMessageConsumer{TKey,TValue}.ConsumeAsync"/>.
    /// </summary>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Any additional Confluent.Kafka consumer configuration key/value pairs.
    /// These are merged on top of the structured properties above.
    /// </summary>
    public Dictionary<string, string> AdditionalConfig { get; set; } = new();

    internal ConsumerConfig ToConfluentConfig()
    {
        var cfg = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset,
            EnableAutoCommit = EnableAutoCommit,
        };
        foreach (var (k, v) in AdditionalConfig)
            cfg.Set(k, v);
        return cfg;
    }
}
