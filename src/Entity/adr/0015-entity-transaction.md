# 0015 — ACID multi-operation transactions via `ITransactionalEntityStore`

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`IEntityStore` exposes individual `SaveAsync` / `DeleteAsync` operations. Application
code that must atomically apply several mutations (e.g. create-an-artist + update-a-label
+ delete-a-stale-draft in one consistent unit) has no primitive to do so. Without a
transaction boundary:

- A partial failure leaves the graph in an inconsistent state.
- Concurrent writers can observe each other's half-applied changes.

The platform is used with two backends:

- **InMemory** (`InMemoryEntityStore`) — a dotNetRDF `Graph` in process memory. Fully
  synchronous; thread-safety must be implemented by the platform, not the library.
- **GraphDB** (`GraphDbEntityStore`) — an Ontotext GraphDB HTTP endpoint that natively
  supports server-side ACID transactions via its REST Transactions API.

## Options

1. **Opt-in `ITransactionalEntityStore : IEntityStore` interface with a fluent
   `EntityTransaction` builder.** Backends that support transactions implement the
   interface; callers check at runtime. InMemory uses a `SemaphoreSlim(1,1)` +
   copy-on-write snapshot for rollback. GraphDB uses the native REST Transactions API.
2. Extend `IEntityStore` directly with `BeginTransactionAsync`. Con: every backend must
   implement it (including future backends where transactions may be unsupported); breaks
   the minimal-interface principle of ADR-0013.
3. Expose transactions only at the `EntityOperations` ambient layer via a callback
   (`CommitTransactionAsync(async tx => { … })`). Pro: ergonomic. Con: callback-based
   patterns are awkward when the body needs to `await` independent async operations;
   the fluent builder pattern is more general.

## Decision

Option 1.

### New abstractions (`Forge.Repository`)

| Type | Role |
|---|---|
| `TransactionOperation` | Abstract base: carries `EntityIri` |
| `EntityWriteOperation` | Abstract sub-base for Create/Update: adds `IEntity Entity` + `WriteMode Mode` for non-generic dispatch |
| `CreateOperation<T>` | Enqueues `SaveAsync(entity, WriteMode.Create)` |
| `UpdateOperation<T>` | Enqueues `SaveAsync(entity, WriteMode.Replace)` |
| `DeleteOperation` | Enqueues `DeleteAsync(iri)` |
| `ITransactionalEntityStore` | Opt-in extension of `IEntityStore`; adds `ExecuteTransactionAsync` |
| `EntityTransaction` | Fluent builder (` .Create() .Update() .Delete()`) + `CommitAsync()`; `IAsyncDisposable` |

### Non-generic dispatch helpers

`IRdfMapper` (non-generic) gains `ProjectEntity(IEntity, IRdfTripleSink, string)`.
`IRdfMapperRegistry` gains `ForEntityType(Type)`. Both are required so backends
can project an entity inside `ExecuteTransactionAsync` without knowing `T` at the call
site (the operation list is `IReadOnlyList<TransactionOperation>`, erasing `T`).

### InMemory strategy

See [Entity.Repository.InMemory ADR-0001](../../Entity.Repository.InMemory/adr/0001-inmemory-transaction-strategy.md).

### GraphDB strategy

See [Entity.Repository.GraphDb ADR-0002](../../Entity.Repository.GraphDb/adr/0002-graphdb-transaction-strategy.md).

### Ambient entry-point

`EntityOperations.BeginTransaction()` guards `ITransactionalEntityStore` capability and
returns a new `EntityTransaction`. See Operations ADR-0004.

## Consequences

- Stores that do not implement `ITransactionalEntityStore` will cause
  `EntityOperations.BeginTransaction()` to throw `NotSupportedException`.
- The change is additive: no existing `IEntityStore` method is modified.
- `IRdfMapper` and `IRdfMapperRegistry` gain one method each; existing implementations
  (`ReflectionRdfMapper<T>`) are updated. Source-generator-emitted mappers (v2) must
  also implement `ProjectEntity`.
