using Confluent.Kafka;

namespace Forge.Messaging.Kafka;

/// <summary>
/// First-class TLS/SASL transport-security settings shared by both
/// <see cref="KafkaProducerOptions"/> and <see cref="KafkaConsumerOptions"/>.
/// See <c>Messaging.Kafka/adr/0001-kafka-security-options.md</c>.
/// </summary>
public sealed class KafkaSecurityOptions
{
    /// <summary>
    /// Protocol used to communicate with brokers.
    /// Leave <c>null</c> to use the Confluent.Kafka default (<c>Plaintext</c>).
    /// </summary>
    public SecurityProtocol? SecurityProtocol { get; set; }

    /// <summary>
    /// SASL mechanism used for client authentication when
    /// <see cref="SecurityProtocol"/> is <c>SaslPlaintext</c> or <c>SaslSsl</c>.
    /// </summary>
    public SaslMechanism? SaslMechanism { get; set; }

    /// <summary>SASL username for <c>Plain</c>, <c>ScramSha256</c>, and <c>ScramSha512</c> mechanisms.</summary>
    public string? SaslUsername { get; set; }

    /// <summary>SASL password for <c>Plain</c>, <c>ScramSha256</c>, and <c>ScramSha512</c> mechanisms.</summary>
    public string? SaslPassword { get; set; }
}
