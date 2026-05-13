using Confluent.Kafka;

namespace Forge.Messaging.Kafka;

/// <summary>
/// Holds the shared <see cref="IProducer{TKey,TValue}"/> instance used by all
/// <see cref="KafkaMessageProducer{TKey,TValue}"/> registrations for a given broker.
/// Registered as a singleton so the Confluent producer's internal connection pool and
/// send buffer are shared across all message types.
/// <para>
/// Implements <see cref="IDisposable"/> to flush and close the producer on shutdown.
/// </para>
/// See root ADR-0020.
/// </summary>
public sealed class KafkaProducerHandle : IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private bool _disposed;

    internal KafkaProducerHandle(KafkaProducerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
            throw new InvalidOperationException(
                $"{nameof(KafkaProducerOptions)}.{nameof(KafkaProducerOptions.BootstrapServers)} must be set.");

        _producer = new ProducerBuilder<string, byte[]>(options.ToConfluentConfig()).Build();
    }

    internal IProducer<string, byte[]> Producer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _producer;
        }
    }

    /// <summary>
    /// Flushes all pending messages and disposes the underlying Confluent producer.
    /// Called automatically by the DI container on application shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }
}
