# 0021 — Entity change event stream

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Consumers outside the platform — read-model projectors, audit logs, downstream services —
have no way to observe entity mutations. There is no durable, ordered record of what changed
and when, and no latest-state snapshot available without querying the RDF store directly.

Two complementary records are needed:

- **Full history** — every create, update, and delete event in insertion order.
- **Compacted state** — the latest known state per entity IRI; a compacted log that can be
  read from the beginning to reconstruct the current world.

The RDF store cannot participate in a distributed transaction with Kafka, so
exactly-once delivery would require an outbox table — additional infrastructure not
justified for v1. At-least-once delivery with idempotent consumers is acceptable.

Entity state must be serialized as a DTO JSON document. The envelope carries metadata so
consumers can act on the event without deserializing the full DTO (e.g. routing by type,
filtering by branch, correlating executions).

All branch graphs are in scope — not only `main`.

## Options

### Option A — Emit inside `ICapabilityHandler`

Pro: natural integration point after business logic.  
Con: entity writes via `IEntityStore` called outside a handler (e.g. seeding, migrations,
direct repository use) are invisible; coverage is incomplete.

### Option B — Application-level interceptor / middleware

Pro: no platform changes.  
Con: shifts responsibility to every consuming application; no SDK guarantee; no
correlation threading.

### Option C — `EventEmittingEntityStore` decorator in the repository chain

Pro: covers every write path uniformly — handlers, seeding, migrations, direct store use;
same registration-order-independent pattern as ADR-0014; correlation is threaded from
`ExecutionCorrelation` ambient scope.  
Con: decorator must sit above AspectEnforcing (emits only after valid, authorized writes)
which means a new keyed-service constant and a second decorator-chain update.

## Decision

Introduce a `Forge.EntityEvents` slice.

### Decorator placement

`EventEmittingEntityStore` wraps the keyed `AspectsTxKey` store (from ADR-0014). A new
keyed constant `ForgeEntityRepositoryBuilder.EventsTxKey = "forge.events.tx"` is added.
The full chain becomes:

```
Guard (AuthorizationGuarding)
  → EventEmitting          ← new, registered at EventsTxKey
    → AspectEnforcing      (AspectsTxKey)
      → Backend            (BackendStoreKey)
```

Registration follows the deferred-resolution pattern of ADR-0014: `AddForgeEntityEvents()`
resolves `AspectsTxKey` at provider-build time, falling back to `BackendStoreKey` when
aspects are not registered. Registration order is arbitrary.

### Envelope

```csharp
public enum EntityChangeOperation { Created, Updated, Deleted }

// TDto is the generated DTO type for the entity.
public sealed record EntityChangedEnvelope<TDto>(
    string Iri,
    string TypeName,            // CLR simple name, e.g. "Artist"
    string TypeIri,             // RDF type IRI
    EntityChangeOperation Operation,
    string BranchIri,
    TDto? Dto,                  // null when Operation == Deleted
    ExecutionCorrelation Correlation,
    DateTimeOffset TimestampUtc);
```

`EntityChangedEnvelope<TDto>` is wrapped in `MessageEnvelope<EntityChangedEnvelope<TDto>>`
(ADR-0020) when published.

### Topic convention

| Topic | Retention policy | Partition key |
|-------|-----------------|---------------|
| `forge.entities.{TypeName}.history` | `retention.ms = -1` (infinite) | Entity IRI |
| `forge.entities.{TypeName}.state` | `cleanup.policy = compact` | Entity IRI |

`TypeName` is the CLR simple name, lower-cased and kebab-cased where applicable
(e.g. `Artist` → `forge.entities.artist.history`).

### Delete tombstones

- **History topic**: a normal `EntityChangedEnvelope<TDto>` with `Operation = Deleted`,
  `Dto = null`. Downstream consumers can read the correlation and timestamp of deletion.
- **State topic**: a Kafka null-value tombstone (key = IRI, value = null) so the
  compaction cleaner removes the record from the compacted log.

Both writes happen in a single producer sequence; the history entry is written first.

### Delivery guarantee

At-least-once. `EventEmittingEntityStore` publishes after the underlying store write has
returned successfully. Consumers must be idempotent, keying on
`Correlation.ExecutionId + Iri + TimestampUtc`.

### Serialization

Entity DTOs are serialized with `System.Text.Json` using the same DTO types emitted by the
Operations source generator. Each slice that opts in to event streaming registers a
`IMessageSerializer<EntityChangedEnvelope<TDto>>` in DI. The envelope `ContentType` is
`application/json`; `SchemaVersion` starts at `1`.

## Consequences

- Every successful entity write — regardless of call site — emits to both Kafka topics.
- Consumers can rebuild current world state by reading the compacted `state` topic from
  offset 0 without touching the RDF store.
- Adding a new entity type to the event stream requires: registering the serializer, no
  code change to the decorator itself.
- The decorator chain constant `EventsTxKey` is public — third-party decorators can slot
  in above or below the event-emitting tier using the same pattern.
- At-least-once implies duplicate events are possible on retry; consumers must handle them.
- `Forge.Entity.Messaging` depends on `Forge.Messaging.Abstractions` and `Forge.Repository`.
  It does not depend on `Forge.Messaging.Kafka` — the Kafka producer is injected via DI.

> *`Forge.EntityEvents` renamed to `Forge.Entity.Messaging` due to ADR-0024.*
