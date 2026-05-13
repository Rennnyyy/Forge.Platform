using Forge.Execution;
using Forge.Messaging.Abstractions;
using Shouldly;

namespace Forge.Messaging.Abstractions.Tests;

public sealed class MessageEnvelopeTests
{
    private static ExecutionCorrelation SomeCorrelation() => new()
    {
        ExecutionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        CallerCorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
    };

    [Fact]
    public void Constructor_sets_all_required_properties()
    {
        var correlation = SomeCorrelation();
        var timestamp = DateTimeOffset.UtcNow;

        var envelope = new MessageEnvelope<string>(
            Topic: "my-topic",
            PartitionKey: "key-1",
            Payload: "hello",
            Correlation: correlation,
            TimestampUtc: timestamp);

        envelope.Topic.ShouldBe("my-topic");
        envelope.PartitionKey.ShouldBe("key-1");
        envelope.Payload.ShouldBe("hello");
        envelope.Correlation.ShouldBe(correlation);
        envelope.TimestampUtc.ShouldBe(timestamp);
    }

    [Fact]
    public void Default_ContentType_is_application_json()
    {
        var envelope = new MessageEnvelope<int>(
            Topic: "t",
            PartitionKey: "k",
            Payload: 42,
            Correlation: SomeCorrelation(),
            TimestampUtc: DateTimeOffset.UtcNow);

        envelope.ContentType.ShouldBe("application/json");
    }

    [Fact]
    public void Default_SchemaVersion_is_1()
    {
        var envelope = new MessageEnvelope<int>(
            Topic: "t",
            PartitionKey: "k",
            Payload: 42,
            Correlation: SomeCorrelation(),
            TimestampUtc: DateTimeOffset.UtcNow);

        envelope.SchemaVersion.ShouldBe(1);
    }

    [Fact]
    public void ContentType_and_SchemaVersion_can_be_overridden()
    {
        var envelope = new MessageEnvelope<int>(
            Topic: "t",
            PartitionKey: "k",
            Payload: 99,
            Correlation: SomeCorrelation(),
            TimestampUtc: DateTimeOffset.UtcNow,
            ContentType: "application/avro",
            SchemaVersion: 3);

        envelope.ContentType.ShouldBe("application/avro");
        envelope.SchemaVersion.ShouldBe(3);
    }

    [Fact]
    public void Record_equality_holds_for_equal_values()
    {
        var correlation = SomeCorrelation();
        var timestamp = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

        var a = new MessageEnvelope<string>("topic", "key", "payload", correlation, timestamp);
        var b = new MessageEnvelope<string>("topic", "key", "payload", correlation, timestamp);

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void Record_inequality_when_topic_differs()
    {
        var correlation = SomeCorrelation();
        var timestamp = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

        var a = new MessageEnvelope<string>("topic-a", "key", "payload", correlation, timestamp);
        var b = new MessageEnvelope<string>("topic-b", "key", "payload", correlation, timestamp);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Payload_supports_complex_type()
    {
        var inner = new SamplePayload(42, "forge");
        var envelope = new MessageEnvelope<SamplePayload>(
            Topic: "complex-topic",
            PartitionKey: "iri:123",
            Payload: inner,
            Correlation: SomeCorrelation(),
            TimestampUtc: DateTimeOffset.UtcNow);

        envelope.Payload.Id.ShouldBe(42);
        envelope.Payload.Name.ShouldBe("forge");
    }

    private sealed record SamplePayload(int Id, string Name);
}
