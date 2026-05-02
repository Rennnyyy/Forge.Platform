# 0001 — SemaphoreSlim + snapshot/restore transaction strategy for InMemoryEntityStore

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`InMemoryEntityStore` wraps a dotNetRDF `Graph` object which is not thread-safe.
`ITransactionalEntityStore.ExecuteTransactionAsync` must provide:

- **Atomicity** — all operations commit or none do.
- **Isolation** — concurrent transactions must not observe each other's partial writes.
- **Consistency/Durability** — the graph is in a consistent state before and after every
  transaction (durability within the process lifetime is implied by in-memory semantics).

## Options

1. **`SemaphoreSlim(1, 1)` for mutual exclusion + triple-level snapshot for rollback.**
   Before execution: snapshot every triple whose subject is any IRI targeted by the
   transaction. On failure: retract new triples for targeted subjects and re-assert the
   snapshot. The semaphore is held for the entire execution window, so no other
   transaction (or concurrent individual `SaveAsync` directly on the same store) can
   interleave.
2. Clone the entire graph before the transaction; swap atomically on success. Pro: simpler
   rollback logic. Con: O(n) clone cost even for tiny transactions against large graphs;
   unnecessary for in-process tests.
3. Use a MVCC-style copy-on-write per subject. Pro: fine-grained. Con: significant
   complexity for a test/embedded backend.

## Decision

Option 1.

- `InMemoryEntityStore` gains a `private readonly SemaphoreSlim _txLock = new(1, 1)`.
- `DisposeAsync` disposes the semaphore.
- `ExecuteTransactionAsync` (in a separate partial-class file `InMemoryTransactionalStore.cs`):
  1. `await _txLock.WaitAsync(ct)`
  2. Snapshot all triples for targeted IRIs into a `Dictionary<IUriNode, List<Triple>>`.
  3. Iterate operations; for each, directly manipulate the underlying dotNetRDF graph
     (no external I/O, so synchronous operations are wrapped in `ValueTask`).
  4. On any exception: call `RestoreSnapshot`, then `_txLock.Release()`, then rethrow.
  5. On success: call `_txLock.Release()`.

Note: individual `SaveAsync` / `DeleteAsync` calls from outside a transaction bypass the
semaphore. Mixing transactional and non-transactional access on the same store instance
is discouraged; the behavioral contract only guarantees isolation between concurrent
`ExecuteTransactionAsync` calls.

## Consequences

- Single-threaded test usage (the common case) incurs only the cost of a non-contended
  `SemaphoreSlim.WaitAsync`.
- Rollback correctly restores blank-node triples because the snapshot captures the full
  `Graph.GetTriplesWithSubject` result, which includes blank-node closures for rdf:List
  objects owned by the subject.
- `DisposeAsync` disposes the semaphore; callers that `await using` the store will not
  leak the handle.
