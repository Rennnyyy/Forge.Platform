# 0010 — Entity messaging demo chapter (Bruno 17-entity-messaging-demo)

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Root ADR-0021 specifies the entity change event stream architecture:
`EventEmittingTransactionalStore` wraps the transactional store, and after each
committed transaction it emits `EntityChangedEnvelope<TEntity>` messages to a pair
of topics (`forge.entities.{type}.history` and `forge.entities.{type}.state`).

The `Application.Sample` project had no runtime demonstration of this mechanism.
All existing Bruno chapters target REST endpoints but none showed that entity
mutations emit observable events on a message broker.

The messaging slices (`Forge.Messaging.InMemory`, `Forge.Entity.Messaging`) were
implemented in prior sessions but not wired into `Program.cs`.

## Decision

Wire the entity messaging stack into `Application.Sample` using the in-memory
broker (no broker infrastructure required) and add a diagnostic endpoint to make
events observable over HTTP.

### Changes to `Program.cs`

1. `AddForgeMessagingInMemory()` — registers `InMemoryMessageBroker` plus open-generic
   `IMessageProducer<,>` / `IMessageConsumer<,>` bindings.
2. `AddForgeEntityEvents()` — installs the `EventEmittingTransactionalStore` decorator.
3. `AddForgeEntityMessaging<Book>(opts => opts.TypeIri = "…")` — opts the `Book` entity
   type into the event stream; topics:
   - `forge.entities.book.history` — immutable audit log (all operations in order)
   - `forge.entities.book.state` — state events consumed by the demo service
4. `EntityEventLog` singleton + `BookEventConsumerService` hosted service — consume
   from the state topic and maintain an in-process list of `EntityEventLogEntry` values.
5. `GET /api/diagnostics/entity-events?iri=…` — returns events filtered by entity IRI
   (or all events when the query string is omitted).

### New source files

| File | Purpose |
|------|---------|
| `Messaging/EntityEventLog.cs` | Thread-safe event log; `EntityEventLogEntry` record |
| `Messaging/BookEventConsumerService.cs` | `BackgroundService` consuming `forge.entities.book.state` |

### New Bruno chapter `17-entity-messaging-demo/`

| File | seq | What it demonstrates |
|------|-----|----------------------|
| `00-setup-create-book.bru` | 1 | `POST api/entities/books` — emits a `Created` event; stores IRI in `messagingBookIri` |
| `01-verify-created-event.bru` | 2 | `GET /api/diagnostics/entity-events?iri=…` — asserts `[0].operation = Created` |
| `02-update-book.bru` | 3 | `PUT api/entities/books?iri=…` — emits an `Updated` event |
| `03-verify-updated-event.bru` | 4 | Diagnostics — asserts `[0]=Created`, `[1].operation = Updated` |
| `04-delete-book.bru` | 5 | `DELETE api/entities/books?iri=…` — emits a `Deleted` event (Dto is null) |
| `05-verify-deleted-event.bru` | 6 | Diagnostics — asserts `[0]=Created`, `[1]=Updated`, `[2].operation = Deleted` |

## Consequences

- The `Book` entity is the only type opted into the event stream in `Application.Sample`.
  Adding more entity types requires one `AddForgeEntityMessaging<T>()` call per type and
  a corresponding consumer service (or a generic hosted service).
- `GET /api/diagnostics/entity-events` is a developer-convenience endpoint. In a
  production deployment it must be removed or placed behind authentication.
- The in-memory broker accumulates events for the lifetime of the process; restarting
  the application clears the log.
- A Kafka backend can be substituted by replacing `AddForgeMessagingInMemory()` with
  `AddForgeMessagingKafka(…)` — `BookEventConsumerService` and `EntityEventLog` are
  transport-agnostic.
