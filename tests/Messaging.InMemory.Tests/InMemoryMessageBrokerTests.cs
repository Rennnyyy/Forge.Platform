using Forge.Execution;
using Forge.Messaging.Abstractions;
using Forge.Messaging.InMemory;
using Forge.Messaging.InMemory.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Forge.Messaging.InMemory.Tests;

public sealed class InMemoryMessageBrokerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static MessageEnvelope<T> MakeEnvelope<T>(string topic, string key, T payload) =>
        new(
            Topic: topic,
            PartitionKey: key,
            Payload: payload,
            Correlation: new ExecutionCorrelation(),
            TimestampUtc: DateTimeOffset.UtcNow);

    /// <summary>
    /// Reads the next message from <paramref name="consumer"/> on <paramref name="topic"/>.
    /// Fails the test if no message arrives within two seconds.
    /// </summary>
    private static async Task<MessageEnvelope<TValue>> ConsumeOneAsync<TValue>(
        IMessageConsumer<string, TValue> consumer,
        string topic)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var envelope in consumer.ConsumeAsync(topic, cts.Token).ConfigureAwait(false))
            return envelope;

        throw new InvalidOperationException("No message received before timeout.");
    }

    // ── broker internals ─────────────────────────────────────────────────────

    [Fact]
    public void GetOrCreate_returns_same_channel_for_same_topic_and_type()
    {
        var broker = new InMemoryMessageBroker();

        var ch1 = broker.GetOrCreate<string>("topic");
        var ch2 = broker.GetOrCreate<string>("topic");

        ch1.ShouldBeSameAs(ch2);
    }

    [Fact]
    public void GetOrCreate_returns_different_channels_for_different_topics()
    {
        var broker = new InMemoryMessageBroker();

        var ch1 = broker.GetOrCreate<string>("topic-a");
        var ch2 = broker.GetOrCreate<string>("topic-b");

        ch1.ShouldNotBeSameAs(ch2);
    }

    [Fact]
    public void GetOrCreate_returns_different_channels_for_different_payload_types()
    {
        var broker = new InMemoryMessageBroker();

        var chString = broker.GetOrCreate<string>("topic");
        var chInt = broker.GetOrCreate<int>("topic");

        ((object)chString).ShouldNotBeSameAs(chInt);
    }

    // ── produce + consume round-trip ─────────────────────────────────────────

    [Fact]
    public async Task Produce_and_consume_round_trips_envelope()
    {
        var broker = new InMemoryMessageBroker();
        var producer = new InMemoryMessageProducer<string, string>(broker);
        var consumer = new InMemoryMessageConsumer<string, string>(broker);
        var sent = MakeEnvelope("test-topic", "iri:1", "hello forge");

        await producer.ProduceAsync(sent);

        var received = await ConsumeOneAsync(consumer, "test-topic");
        received.ShouldBe(sent);
    }

    [Fact]
    public async Task Multiple_messages_arrive_in_order()
    {
        var broker = new InMemoryMessageBroker();
        var producer = new InMemoryMessageProducer<string, int>(broker);
        var consumer = new InMemoryMessageConsumer<string, int>(broker);
        const string topic = "order-topic";

        await producer.ProduceAsync(MakeEnvelope(topic, "k", 1));
        await producer.ProduceAsync(MakeEnvelope(topic, "k", 2));
        await producer.ProduceAsync(MakeEnvelope(topic, "k", 3));

        var first = await ConsumeOneAsync(consumer, topic);
        var second = await ConsumeOneAsync(consumer, topic);
        var third = await ConsumeOneAsync(consumer, topic);

        first.Payload.ShouldBe(1);
        second.Payload.ShouldBe(2);
        third.Payload.ShouldBe(3);
    }

    // ── topic isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Different_topics_are_isolated()
    {
        var broker = new InMemoryMessageBroker();
        var producer = new InMemoryMessageProducer<string, string>(broker);
        var consumer = new InMemoryMessageConsumer<string, string>(broker);

        var envA = MakeEnvelope("topic-a", "k", "A");
        var envB = MakeEnvelope("topic-b", "k", "B");

        await producer.ProduceAsync(envA);
        await producer.ProduceAsync(envB);

        var fromA = await ConsumeOneAsync(consumer, "topic-a");
        var fromB = await ConsumeOneAsync(consumer, "topic-b");

        fromA.Payload.ShouldBe("A");
        fromB.Payload.ShouldBe("B");
    }

    // ── reset behaviour ──────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_causes_active_consumer_to_complete()
    {
        var broker = new InMemoryMessageBroker();
        var consumer = new InMemoryMessageConsumer<string, string>(broker);
        const string topic = "reset-topic";

        // Ensure the channel exists before reset.
        _ = broker.GetOrCreate<string>(topic);

        var messages = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var consumeTask = Task.Run(async () =>
        {
            await foreach (var env in consumer.ConsumeAsync(topic, cts.Token).ConfigureAwait(false))
                messages.Add(env.Payload);
        }, cts.Token);

        // Let the consumer start waiting.
        await Task.Delay(50);

        // Reset completes all channel writers — consumer should exit naturally.
        broker.Reset();

        await consumeTask.WaitAsync(TimeSpan.FromSeconds(2));

        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_reset_new_produce_is_visible_to_new_consumer()
    {
        var broker = new InMemoryMessageBroker();
        var producer = new InMemoryMessageProducer<string, string>(broker);
        var consumer = new InMemoryMessageConsumer<string, string>(broker);
        const string topic = "reset-fresh-topic";

        await producer.ProduceAsync(MakeEnvelope(topic, "k", "before-reset"));
        broker.Reset();

        await producer.ProduceAsync(MakeEnvelope(topic, "k", "after-reset"));

        var received = await ConsumeOneAsync(consumer, topic);
        received.Payload.ShouldBe("after-reset");
    }

    // ── cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ConsumeAsync_respects_already_cancelled_token()
    {
        var broker = new InMemoryMessageBroker();
        var consumer = new InMemoryMessageConsumer<string, string>(broker);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in consumer.ConsumeAsync("cancel-topic", cts.Token).ConfigureAwait(false))
            {
                // should not reach here
            }
        });
    }

    [Fact]
    public async Task ProduceAsync_throws_when_cancelled()
    {
        var broker = new InMemoryMessageBroker();
        var producer = new InMemoryMessageProducer<string, string>(broker);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await producer.ProduceAsync(MakeEnvelope("t", "k", "v"), cts.Token));
    }

    // ── DI registration ──────────────────────────────────────────────────────

    [Fact]
    public void DI_registration_resolves_producer_and_consumer()
    {
        var services = new ServiceCollection();
        services.AddForgeMessagingInMemory();
        var provider = services.BuildServiceProvider();

        var producer = provider.GetRequiredService<IMessageProducer<string, string>>();
        var consumer = provider.GetRequiredService<IMessageConsumer<string, string>>();

        producer.ShouldNotBeNull();
        consumer.ShouldNotBeNull();
        producer.ShouldBeOfType<InMemoryMessageProducer<string, string>>();
        consumer.ShouldBeOfType<InMemoryMessageConsumer<string, string>>();
    }

    [Fact]
    public void DI_registration_shares_single_broker_instance()
    {
        var services = new ServiceCollection();
        services.AddForgeMessagingInMemory();
        var provider = services.BuildServiceProvider();

        var broker1 = provider.GetRequiredService<InMemoryMessageBroker>();
        var broker2 = provider.GetRequiredService<InMemoryMessageBroker>();

        broker1.ShouldBeSameAs(broker2);
    }

    [Fact]
    public void DI_AddForgeMessagingInMemory_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddForgeMessagingInMemory();
        services.AddForgeMessagingInMemory(); // second call — must not throw or duplicate

        var provider = services.BuildServiceProvider();

        // TryAdd semantics mean only one registration per type survives.
        var producers = provider.GetServices<IMessageProducer<string, string>>().ToList();
        producers.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DI_resolved_producer_and_consumer_share_broker_and_can_round_trip()
    {
        var services = new ServiceCollection();
        services.AddForgeMessagingInMemory();
        var provider = services.BuildServiceProvider();

        var producer = provider.GetRequiredService<IMessageProducer<string, string>>();
        var consumer = provider.GetRequiredService<IMessageConsumer<string, string>>();
        var sent = MakeEnvelope("di-topic", "k", "di-payload");

        await producer.ProduceAsync(sent);

        var received = await ConsumeOneAsync(consumer, "di-topic");
        received.ShouldBe(sent);
    }
}
