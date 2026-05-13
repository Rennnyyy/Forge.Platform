using Forge.Execution;
using Forge.Messaging.Abstractions;
using Forge.Messaging.Kafka;
using Forge.Messaging.Kafka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Messaging.Kafka.Tests;

internal sealed record Payload(string Value);

/// <summary>
/// Unit tests for the Kafka messaging slice.
/// These tests do NOT connect to a real Kafka broker; they cover:
/// - DI registration and option validation
/// - JSON serializer round-trip
/// - KafkaProducerOptions / KafkaConsumerOptions mapping to Confluent config
/// </summary>
public sealed class JsonMessageSerializerTests
{
    [Fact]
    public void Serialize_then_Deserialize_roundtrips_value()
    {
        var serializer = new JsonMessageSerializer<Payload>();
        var payload = new Payload("hello");

        var bytes = serializer.Serialize(payload);
        var result = serializer.Deserialize(bytes);

        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void Serialize_produces_utf8_json()
    {
        var serializer = new JsonMessageSerializer<Payload>();
        var bytes = serializer.Serialize(new Payload("world"));
        var json = System.Text.Encoding.UTF8.GetString(bytes.Span);

        json.ShouldContain("world");
    }

    [Fact]
    public void Serialize_envelope_then_Deserialize_roundtrips_full_envelope()
    {
        var serializer = new JsonMessageSerializer<MessageEnvelope<Payload>>();
        var correlation = new ExecutionCorrelation();
        var ts = DateTimeOffset.UtcNow;
        var envelope = new MessageEnvelope<Payload>(
            Topic: "test.topic",
            PartitionKey: "pk-1",
            Payload: new Payload("data"),
            Correlation: correlation,
            TimestampUtc: ts);

        var bytes = serializer.Serialize(envelope);
        var result = serializer.Deserialize(bytes);

        result.Topic.ShouldBe("test.topic");
        result.PartitionKey.ShouldBe("pk-1");
        result.Payload.Value.ShouldBe("data");
        result.Correlation.ExecutionId.ShouldBe(correlation.ExecutionId);
        result.TimestampUtc.ShouldBe(ts);
    }

    [Fact]
    public void Deserialize_throws_when_null_result()
    {
        var serializer = new JsonMessageSerializer<Payload?>();
        var bytes = System.Text.Encoding.UTF8.GetBytes("null");
        Should.Throw<InvalidOperationException>(() => serializer.Deserialize(bytes));
    }
}

public sealed class KafkaProducerOptionsTests
{
    [Fact]
    public void ToConfluentConfig_maps_BootstrapServers()
    {
        var opts = new KafkaProducerOptions { BootstrapServers = "broker:9092" };
        var cfg = opts.ToConfluentConfig();
        cfg.BootstrapServers.ShouldBe("broker:9092");
    }

    [Fact]
    public void ToConfluentConfig_maps_additional_config()
    {
        var opts = new KafkaProducerOptions
        {
            BootstrapServers = "broker:9092",
            AdditionalConfig = new() { ["linger.ms"] = "5" },
        };
        var cfg = opts.ToConfluentConfig();
        cfg.Get("linger.ms").ShouldBe("5");
    }

    [Fact]
    public void Default_options_have_idempotence_enabled_and_acks_all()
    {
        var opts = new KafkaProducerOptions();
        opts.EnableIdempotence.ShouldBeTrue();
        opts.Acks.ShouldBe(Confluent.Kafka.Acks.All);
        opts.MaxInFlight.ShouldBe(1);
    }
}

public sealed class KafkaConsumerOptionsTests
{
    [Fact]
    public void ToConfluentConfig_maps_BootstrapServers_and_GroupId()
    {
        var opts = new KafkaConsumerOptions
        {
            BootstrapServers = "broker:9092",
            GroupId = "my-group",
        };
        var cfg = opts.ToConfluentConfig();
        cfg.BootstrapServers.ShouldBe("broker:9092");
        cfg.GroupId.ShouldBe("my-group");
    }

    [Fact]
    public void Default_options_disable_auto_commit()
    {
        var opts = new KafkaConsumerOptions();
        opts.EnableAutoCommit.ShouldBeFalse();
    }

    [Fact]
    public void ToConfluentConfig_maps_additional_config()
    {
        var opts = new KafkaConsumerOptions
        {
            BootstrapServers = "broker:9092",
            GroupId = "g",
            AdditionalConfig = new() { ["max.poll.interval.ms"] = "30000" },
        };
        var cfg = opts.ToConfluentConfig();
        cfg.Get("max.poll.interval.ms").ShouldBe("30000");
    }
}

public sealed class KafkaDiTests
{
    private static IServiceCollection BuildServices(
        string bootstrap = "broker:9092",
        string groupId = "test-group")
    {
        var services = new ServiceCollection();
        services.AddForgeMessagingKafka(
            p => p.BootstrapServers = bootstrap,
            c =>
            {
                c.BootstrapServers = bootstrap;
                c.GroupId = groupId;
            });
        return services;
    }

    [Fact]
    public void Resolves_KafkaProducerOptions()
    {
        var sp = BuildServices().BuildServiceProvider();
        var opts = sp.GetRequiredService<KafkaProducerOptions>();
        opts.BootstrapServers.ShouldBe("broker:9092");
    }

    [Fact]
    public void Resolves_KafkaConsumerOptions()
    {
        var sp = BuildServices().BuildServiceProvider();
        var opts = sp.GetRequiredService<KafkaConsumerOptions>();
        opts.GroupId.ShouldBe("test-group");
    }

    [Fact]
    public void Resolves_open_generic_IMessageSerializer()
    {
        var sp = BuildServices().BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer<Payload>>();
        serializer.ShouldNotBeNull();
        serializer.ShouldBeOfType<JsonMessageSerializer<Payload>>();
    }

    [Fact]
    public void Resolves_open_generic_IMessageDeserializer()
    {
        var sp = BuildServices().BuildServiceProvider();
        var deserializer = sp.GetRequiredService<IMessageDeserializer<Payload>>();
        deserializer.ShouldNotBeNull();
        deserializer.ShouldBeOfType<JsonMessageSerializer<Payload>>();
    }

    [Fact]
    public void Throws_when_producer_BootstrapServers_empty()
    {
        var services = new ServiceCollection();
        Should.Throw<InvalidOperationException>(() =>
            services.AddForgeMessagingKafka(
                p => { /* BootstrapServers not set */ },
                c => { c.BootstrapServers = "b:9092"; c.GroupId = "g"; }));
    }

    [Fact]
    public void Throws_when_consumer_BootstrapServers_empty()
    {
        var services = new ServiceCollection();
        Should.Throw<InvalidOperationException>(() =>
            services.AddForgeMessagingKafka(
                p => p.BootstrapServers = "b:9092",
                c => { c.GroupId = "g"; /* BootstrapServers not set */ }));
    }

    [Fact]
    public void Throws_when_consumer_GroupId_empty()
    {
        var services = new ServiceCollection();
        Should.Throw<InvalidOperationException>(() =>
            services.AddForgeMessagingKafka(
                p => p.BootstrapServers = "b:9092",
                c => c.BootstrapServers = "b:9092" /* GroupId not set */));
    }

    [Fact]
    public void TryAdd_semantics_do_not_overwrite_existing_producer()
    {
        var services = new ServiceCollection();

        // Register a stub first.
        var stub = new StubProducer();
        services.AddSingleton<IMessageProducer<string, Payload>>(stub);

        // Then register Kafka — should not replace the stub.
        services.AddForgeMessagingKafka(
            p => p.BootstrapServers = "b:9092",
            c => { c.BootstrapServers = "b:9092"; c.GroupId = "g"; });

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IMessageProducer<string, Payload>>().ShouldBeSameAs(stub);
    }

    // Minimal stub for TryAdd test
    private sealed class StubProducer : IMessageProducer<string, Payload>
    {
        public ValueTask ProduceAsync(
            MessageEnvelope<Payload> envelope,
            CancellationToken cancellationToken = default) => default;
    }
}
