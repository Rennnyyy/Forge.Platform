# 0020 — Messaging abstractions slice

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Two forthcoming features — entity change event streaming (ADR-0021) and async capability
dispatch (ADR-0022) — both need to publish and consume messages with an external broker.
Without a shared foundation the two features would define parallel but divergent producer,
consumer, and envelope primitives, violating the single-vocabulary principle already
established for HTTP (ADR-0017).

Kafka is the primary target broker. Unit and integration tests need a deterministic in-memory
substitute so no Kafka broker is required for `dotnet test`.

The SDK must not prescribe deployment topology. Consuming applications decide how many topics
to create, whether to use Schema Registry, and how to host consumer loops.

## Options

### Option A — Each feature slice owns its own broker primitives

Pro: zero shared dependency.  
Con: duplicated envelope shapes, duplicated Kafka configuration, inconsistent correlation
threading; SDK consumers face two incompatible messaging vocabularies.

### Option B — Shared primitives in `Forge.Messaging.Abstractions`

Pro: single envelope contract, single correlation model, interchangeable InMemory/Kafka
backends for tests.  
Con: creates a new cross-cutting dependency that every messaging slice must take.  
Con is acceptable — the same pattern already exists for `Forge.Execution` and
`Forge.Operations.Http`.

### Option C — Shared primitives inside `Forge.Execution`

Pro: no new project.  
Con: `Forge.Execution` carries HTTP-correlation semantics; embedding broker concerns there
blurs the layering.

## Decision

Add three new slices:

| Slice | Target | Purpose |
|-------|--------|---------|
| `Forge.Messaging.Abstractions` | `net10.0` | Broker-agnostic interfaces and shared envelope records. Zero broker dependency. |
| `Forge.Messaging.Kafka` | `net10.0` | Confluent.Kafka implementation of the abstractions. |
| `Forge.Messaging.InMemory` | `net10.0` | In-process channel-based implementation for tests and samples. |

### Core types in `Forge.Messaging.Abstractions`

```csharp
// Opaque in-flight message envelope. Broker-agnostic.
public sealed record MessageEnvelope<TValue>(
    string Topic,
    string PartitionKey,
    TValue Payload,
    ExecutionCorrelation Correlation,
    DateTimeOffset TimestampUtc,
    string ContentType = "application/json",
    int SchemaVersion = 1);

// Publish one message. TKey is the Kafka partition key type (string in all platform uses).
public interface IMessageProducer<TKey, TValue>
{
    ValueTask ProduceAsync(
        string topic,
        TKey key,
        TValue value,
        MessageEnvelope<TValue> envelope,
        CancellationToken cancellationToken = default);
}
```

> **Implementation note (ADR-0009 inline update):** The shipped `IMessageProducer<TKey, TValue>`
> simplified `ProduceAsync` to accept only `(MessageEnvelope<TValue> envelope, CancellationToken)`.
> The `string topic`, `TKey key`, and `TValue value` parameters listed above were dropped before
> the first release — they duplicate information already carried inside `MessageEnvelope<TValue>`,
> and their presence would force callers to decompose and re-specify data the envelope already holds.

```csharp
// Consume messages as an async stream. Application owns the hosting loop.
public interface IMessageConsumer<TKey, TValue>
{
    IAsyncEnumerable<MessageEnvelope<TValue>> ConsumeAsync(
        string topic,
        CancellationToken cancellationToken = default);
}

// Serialization seam — implemented by System.Text.Json adapter in each feature slice.
public interface IMessageSerializer<T>   { ReadOnlyMemory<byte> Serialize(T value); }
public interface IMessageDeserializer<T> { T Deserialize(ReadOnlyMemory<byte> bytes); }
```

`ExecutionCorrelation` is re-used from `Forge.Execution`; no new correlation type is
introduced. `MessageEnvelope<TValue>` is the single shared envelope record for all
platform messaging — both entity events and capability commands/replies use it.

### Kafka implementation contract

`Forge.Messaging.Kafka` provides `KafkaMessageProducer<TKey, TValue>` and
`KafkaMessageConsumer<TKey, TValue>`. Kafka-specific concerns (bootstrap servers, SASL,
schema registry, `acks`, `enable.idempotence`) live entirely in `Forge.Messaging.Kafka`
and are invisible to callers of the abstractions.

### InMemory implementation contract

`Forge.Messaging.InMemory` provides `InMemoryMessageBroker` backed by
`System.Threading.Channels`. It supports multiple named topics, unbounded or bounded
channels, and reset between test runs. No external process required.

## Consequences

- All platform messaging uses `MessageEnvelope<TValue>` as its wire contract — consistent
  with the shared-contract principle of ADR-0017.
- Feature slices (`Forge.Entity.Messaging`, `Forge.Capability.Messaging`) depend on
  `Forge.Messaging.Abstractions` only; no Kafka reference leaks into those slices.
- Tests in any feature slice run against `InMemoryMessageBroker` via DI swap.
- Applications that need a broker other than Kafka can implement
  `IMessageProducer` / `IMessageConsumer` without touching platform code.
- `SchemaVersion` in the envelope is a forward-compatibility seam; v1 uses plain JSON;
  Avro or Protobuf migration can increment the version without a breaking API change.

> *`Forge.EntityEvents` renamed to `Forge.Entity.Messaging` due to ADR-0024.*
