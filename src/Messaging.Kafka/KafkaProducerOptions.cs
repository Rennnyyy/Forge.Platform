using Confluent.Kafka;

namespace Forge.Messaging.Kafka;

/// <summary>
/// Configuration for the Kafka producer.
/// Consumed by <see cref="KafkaMessageProducer{TKey,TValue}"/>.
/// See root ADR-0020.
/// </summary>
public sealed class KafkaProducerOptions
{
    /// <summary>
    /// Comma-separated list of Kafka bootstrap servers.
    /// Example: <c>localhost:9092</c> or <c>broker1:9092,broker2:9092</c>.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-producer delivery acknowledgement level.
    /// Defaults to <see cref="Acks.All"/> (strongest guarantee).
    /// </summary>
    public Acks Acks { get; set; } = Acks.All;

    /// <summary>
    /// Maximum number of in-flight produce requests per connection.
    /// Set to 1 to preserve message ordering when <see cref="Acks"/> is All.
    /// </summary>
    public int MaxInFlight { get; set; } = 1;

    /// <summary>
    /// Whether to enable idempotent production. Requires <see cref="Acks"/> == All
    /// and <see cref="MaxInFlight"/> &lt;= 5.
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Any additional Confluent.Kafka producer configuration key/value pairs.
    /// These are merged on top of the structured properties above.
    /// </summary>
    public Dictionary<string, string> AdditionalConfig { get; set; } = new();

    internal ProducerConfig ToConfluentConfig()
    {
        var cfg = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            Acks = Acks,
            MaxInFlight = MaxInFlight,
            EnableIdempotence = EnableIdempotence,
        };
        foreach (var (k, v) in AdditionalConfig)
            cfg.Set(k, v);
        return cfg;
    }
}
