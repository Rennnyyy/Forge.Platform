# 0022 — Async capability and operations command bus

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

All capability dispatches and entity CRUD operations are synchronous today. There is no
durable queue behind any execution path and no way to offload work to a separate process,
fan out to multiple consumers, or await a reply that arrives asynchronously.

Two dispatch patterns are needed:

- **Fire-and-forget** — publish the command and return immediately; useful for background
  tasks and one-way notifications.
- **Request-reply** — publish the command, then await the `ExecutionResult<TResponse>` when
  it arrives on a reply channel; useful for API gateways that need a synchronous-feeling
  answer backed by durable async execution.

Entity CRUD operations (Operations slice) must route through the same bus — not a separate
async track. The SDK must not impose a second execution model for writes.

Forge.Platform is an SDK. It does not know whether event-processing will happen in the same
process, a worker sidecar, or a fleet of consumers. The SDK exposes the full set of
primitives; applications choose the topology.

The existing `ICapabilityHandler<TCommand, TResponse>` contract — including aspect
validation, `ExecutionResult.Events` emission, and entity-event fan-out via ADR-0021 — must
be preserved unchanged on the consumer side.

## Options

### Option A — Async overload on `ICapabilityDispatcher`

Pro: single interface.  
Con: conflates in-process dispatch with remote dispatch; no way to inject a different
transport without replacing the entire dispatcher.

### Option B — Separate `IAsyncCapabilityDispatcher` in existing `Forge.Capability`

Pro: co-located with the synchronous dispatcher.  
Con: `Forge.Capability` gains a dependency on `Forge.Messaging.Abstractions`; async
dispatch is optional infrastructure, not a core capability concept.

### Option C — New `Forge.Capability.Messaging` slice

Pro: clean separation; `Forge.Capability` stays broker-free; Kafka/InMemory can be swapped
via DI without touching the capability slice; consistent with ADR-0008 satellite naming.  
Con: one more project reference for applications that adopt async dispatch.

## Decision

Add a new `Forge.Capability.Messaging` slice.

### Envelope records

```csharp
// Published by the producer (dispatcher side).
public sealed record CapabilityCommandEnvelope<TCommand>(
    TCommand Command,
    ExecutionCorrelation Correlation,   // Correlation.ExecutionId doubles as correlation key
    string? AspectIri,
    string? ReplyToTopic,               // null for fire-and-forget
    DateTimeOffset TimestampUtc)
    where TCommand : class;

// Published by the consumer (handler side) onto ReplyToTopic.
public sealed record CapabilityReplyEnvelope<TResponse>(
    ExecutionResult<TResponse> Result,
    ExecutionCorrelation Correlation,
    DateTimeOffset TimestampUtc)
    where TResponse : class;
```

Both records are wrapped in `MessageEnvelope<T>` (ADR-0020) when published.
This is the single shared contract for all capability messaging — consistent with
ADR-0017's single-vocabulary principle applied to the messaging layer.

### Dispatcher

```csharp
public interface IAsyncCapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    // Fire-and-forget. No reply channel.
    ValueTask PublishAsync(
        TCommand command,
        string? aspectIri = null,
        CancellationToken cancellationToken = default);

    // Request-reply. Awaits result up to the given timeout.
    Task<ExecutionResult<TResponse>> PublishAndWaitAsync(
        TCommand command,
        string? aspectIri = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
```

`PublishAndWaitAsync` sets `ReplyToTopic` on the envelope and registers a
`TaskCompletionSource<ExecutionResult<TResponse>>` keyed by `ExecutionId` in an in-process
`PendingReplyRegistry`. A companion `CapabilityReplyListener<TResponse>` (see below)
completes that source when the reply arrives. On timeout the source is faulted with an
`ExecutionResult.Fail` carrying error code `BrokerReplyTimeout`.

The SDK does not mandate reply-topic topology (per-instance ephemeral vs. shared). The
application registers `CapabilityReplyListener` against the topic name of its choice;
`ReplyToTopic` in the envelope is a string that the infrastructure layer interprets.

### Consumer

```csharp
// SDK-provided consumer. Application or framework owns the hosting loop.
public interface ICapabilityMessageConsumer<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    // Reads one command envelope, delegates to the in-process handler, publishes reply.
    ValueTask ConsumeOneAsync(
        MessageEnvelope<CapabilityCommandEnvelope<TCommand>> envelope,
        CancellationToken cancellationToken = default);
}
```

`ICapabilityMessageConsumer` delegates to the registered `ICapabilityHandler<TCommand, TResponse>`.
All existing aspect validation, `CapabilityContext` event collection, and `ExecutionResult`
semantics execute inside the handler — unchanged. After the handler returns,
`ICapabilityMessageConsumer` publishes a `CapabilityReplyEnvelope` to `ReplyToTopic` if it
is non-null.

### Entity CRUD on the bus

Entity CRUD operations route through the bus via thin
`ICapabilityHandler<TEntityCommand, ExecutionResult<TEntityResponse>>` adapters generated
(or hand-written) per entity type. No separate async track exists for CRUD. This preserves
the single entry-point invariant: every mutation goes through a handler; every handler emits
`ExecutionResult.Events`; every event hits aspect validation; entity-event fan-out (ADR-0021)
fires through the repository decorator chain regardless.

### DI registration helpers

```csharp
// Producer side (publishes commands)
services.AddForgeCapabilityMessaging<CreateArtistCommand, ArtistResponse>(options => {
    options.CommandTopic = "forge.capabilities.create-artist.commands";
    options.ReplyTopic   = "forge.capabilities.create-artist.replies";
});

// Consumer side (handles commands, publishes replies)
services.AddForgeCapabilityConsumer<CreateArtistCommand, ArtistResponse>(options => {
    options.CommandTopic = "forge.capabilities.create-artist.commands";
    options.ReplyTopic   = "forge.capabilities.create-artist.replies";
});
```

The SDK supplies no opinion on whether the consumer runs in the same host as the producer.

## Consequences

- Fire-and-forget and request-reply are supported from the same interface and the same
  command/reply envelope records.
- In-process `ICapabilityHandler` logic is reused verbatim by the consumer — no
  duplication of business logic.
- Applications can mix synchronous dispatch (`ICapabilityDispatcher`) and async dispatch
  (`IAsyncCapabilityDispatcher`) side-by-side; both share the same handler registration.
- The reply-topology decision (ephemeral vs. shared topic) is deferred to each deploying
  application; the SDK provides `PendingReplyRegistry` as the in-process completion
  mechanism regardless of topology.
- `Forge.Capability.Messaging` depends on `Forge.Messaging.Abstractions` and
  `Forge.Capability`. It does not depend on `Forge.Messaging.Kafka`.
- Entity-event fan-out (ADR-0021) and command-reply fan-out are independent: a capability
  that writes entities will produce both entity-change events and a capability reply
  envelope without any additional wiring.
