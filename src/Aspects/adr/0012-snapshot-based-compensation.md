# 0012 — Snapshot-based compensation in `AspectEnforcingTransactionalStore`

- **Status**: accepted
- **Date**: 2026-05-09
- **Author**: agent

## Context

`AspectEnforcingTransactionalStore.ExecuteTransactionAsync` processes operations one at a
time: validate (SHACL local + SPARQL context), then apply via a single-op inner transaction.
This allows each operation to observe the progressive state from prior operations in the same
batch (ADR-0001 queue-order semantics).

When a later operation fails validation, the already-applied operations must be rolled back.
The previous compensation mechanism had two bugs:

1. **Dead pattern match** — `if (op is CreateOperation<IEntity>)` never fires for real entity
   types because C# generic classes are invariant: `CreateOperation<Artist>` is not
   `CreateOperation<IEntity>`. As a result, the `applied` list always produced an empty
   compensation list and no rollback ever occurred.

2. **No snapshot for Update/Delete** — even if the pattern match had worked, pre-operation
   snapshots of the entity state were not captured for `UpdateOperation` or typed
   `DeleteOperation`, so full restore was impossible.

The combined effect: any transaction where an earlier operation was successfully applied
before a later operation failed validation left the store in a permanently inconsistent
intermediate state.

## Decision

Use **snapshot-before-apply closures** instead of the previous `applied + BuildCompensations`
approach.

### Capture protocol

Before applying each operation, capture an undo closure:

| Operation | Pre-apply load | Undo closure |
|---|---|---|
| `EntityWriteOperation` (Create) | None | `ExecuteTransactionAsync([new DeleteOperation(iri)])` |
| `EntityWriteOperation` (Update) | `LoadAsync<T>(iri)` → `snapshot` | If snapshot not null: `ExecuteTransactionAsync([new UpdateOperation<T>(snapshot)])`; if null: `ExecuteTransactionAsync([new DeleteOperation(iri)])` |
| `DeleteOperation` with `EntityType != null` | `LoadAsync<T>(iri)` → `snapshot` | If snapshot not null: `ExecuteTransactionAsync([new UpdateOperation<T>(snapshot)])`; if null: `null` (entity was absent; nothing to restore) |
| `DeleteOperation` with `EntityType == null` | None | `null` (no type info; undo is a no-op) |

Undo closures are pushed onto a `Stack<Func<CancellationToken, ValueTask>>` in forward
order and popped in LIFO order (= reverse application order) during rollback.

### Snapshot captor infrastructure

`CaptureUndoAsync` needs to call `_inner.LoadAsync<T>(iri, ct)`, but the entity type `T`
is only known at runtime (from `write.Entity.GetType()` or `del.EntityType`). A private
`ISnapshotCaptor` / `SnapshotCaptor<T>` pair (nested types) is used to dispatch the typed
call without reflection at the call site. `SnapshotCaptor<T>` instances are cached in a
static `ConcurrentDictionary<Type, ISnapshotCaptor>` keyed by CLR type to amortise the
`MakeGenericType` instantiation cost.

### Undo ordering

Undo closures call `_inner.ExecuteTransactionAsync` directly (bypassing the aspect and
authorization decorator stack), correct because:

- Compensations must not trigger re-validation of the original operation's aspect.
- The inner backend's atomicity is not required here; each undo is one operation.

Individual compensation errors are swallowed so the original exception propagates.

### Limitation for untyped deletes

`EntityTransaction.Delete(iri)` creates a `DeleteOperation` with `EntityType == null`.
No snapshot can be captured without knowing the entity type, so this delete is not
compensable. Callers that need delete rollback should use
`EntityTransaction.Delete<T>(iri, aspectIri)`, which sets `DeleteOperation.EntityType`.

## Consequences

- Mixed transactions (`Update(A) → Create(B)` where B fails validation) now correctly
  restore A to its pre-transaction state.
- `CreateOperation<T>` compensation no longer uses a type-parameter pattern match and
  requires no load — the undo simply deletes the IRI.
- A `LoadAsync<T>` round-trip is incurred for every non-Create operation in the
  transaction (one load per op). For backends that support in-process reads without
  network I/O (e.g. `InMemoryEntityStore`), this is negligible. For `GraphDbEntityStore`
  over HTTP, each load is one CONSTRUCT query. A future optimization could skip snapshot
  loading for operations whose aspect is NoOp, but this is deferred.
- The `AspectEnforcingTransactionalStore` continues to implement only
  `ITransactionalEntityStore`; no new public surface is added.
