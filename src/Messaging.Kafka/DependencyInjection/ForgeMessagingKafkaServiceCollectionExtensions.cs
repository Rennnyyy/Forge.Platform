using Forge.Messaging.Abstractions;
using Forge.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.Messaging.Kafka.DependencyInjection;

/// <summary>
/// DI extensions for the Kafka messaging implementation.
/// See root ADR-0020.
/// </summary>
public static class ForgeMessagingKafkaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Kafka-backed <see cref="IMessageProducer{TKey,TValue}"/> and
    /// <see cref="IMessageConsumer{TKey,TValue}"/> implementations as the platform
    /// messaging transport.
    /// <para>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="KafkaProducerHandle"/> — singleton shared Confluent producer.</item>
    ///   <item>Open-generic <see cref="IMessageProducer{TKey,TValue}"/> → <see cref="KafkaMessageProducer{TKey,TValue}"/>.</item>
    ///   <item>Open-generic <see cref="IMessageConsumer{TKey,TValue}"/> → <see cref="KafkaMessageConsumer{TKey,TValue}"/>.</item>
    ///   <item>Open-generic <see cref="IMessageSerializer{T}"/> and <see cref="IMessageDeserializer{T}"/> → <see cref="JsonMessageSerializer{T}"/> (JSON / UTF-8).</item>
    /// </list>
    /// Uses <c>TryAdd</c> semantics — existing registrations are not overwritten, so
    /// applications can substitute custom serializers or broker implementations before
    /// calling this method.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureProducer">
    /// Callback to configure <see cref="KafkaProducerOptions"/>.
    /// <see cref="KafkaProducerOptions.BootstrapServers"/> is required.
    /// </param>
    /// <param name="configureConsumer">
    /// Callback to configure <see cref="KafkaConsumerOptions"/>.
    /// <see cref="KafkaConsumerOptions.BootstrapServers"/> and
    /// <see cref="KafkaConsumerOptions.GroupId"/> are required.
    /// </param>
    public static IServiceCollection AddForgeMessagingKafka(
        this IServiceCollection services,
        Action<KafkaProducerOptions> configureProducer,
        Action<KafkaConsumerOptions> configureConsumer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProducer);
        ArgumentNullException.ThrowIfNull(configureConsumer);

        var producerOpts = new KafkaProducerOptions();
        configureProducer(producerOpts);

        var consumerOpts = new KafkaConsumerOptions();
        configureConsumer(consumerOpts);

        ValidateProducerOptions(producerOpts);
        ValidateConsumerOptions(consumerOpts);

        // Singleton options instances — consumers of the options can inject directly.
        services.TryAddSingleton(producerOpts);
        services.TryAddSingleton(consumerOpts);

        // Shared producer handle (owns the Confluent IProducer connection pool).
        services.TryAddSingleton(sp =>
            new KafkaProducerHandle(sp.GetRequiredService<KafkaProducerOptions>()));

        // Open-generic registrations for producers and consumers.
        services.TryAddSingleton(typeof(IMessageProducer<,>), typeof(KafkaMessageProducer<,>));
        services.TryAddSingleton(typeof(IMessageConsumer<,>), typeof(KafkaMessageConsumer<,>));

        // Default JSON serializer / deserializer (open-generic).
        services.TryAddSingleton(typeof(IMessageSerializer<>), typeof(JsonMessageSerializer<>));
        services.TryAddSingleton(typeof(IMessageDeserializer<>), typeof(JsonMessageSerializer<>));

        return services;
    }

    // ── validation ────────────────────────────────────────────────────────────

    private static void ValidateProducerOptions(KafkaProducerOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.BootstrapServers))
            throw new InvalidOperationException(
                $"AddForgeMessagingKafka: {nameof(KafkaProducerOptions.BootstrapServers)} must be set.");
    }

    private static void ValidateConsumerOptions(KafkaConsumerOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.BootstrapServers))
            throw new InvalidOperationException(
                $"AddForgeMessagingKafka: {nameof(KafkaConsumerOptions.BootstrapServers)} must be set.");
        if (string.IsNullOrWhiteSpace(opts.GroupId))
            throw new InvalidOperationException(
                $"AddForgeMessagingKafka: {nameof(KafkaConsumerOptions.GroupId)} must be set.");
    }
}
