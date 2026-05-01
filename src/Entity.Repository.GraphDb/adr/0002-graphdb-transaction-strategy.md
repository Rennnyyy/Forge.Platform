# 0002 — GraphDB REST Transactions API for ACID transaction support

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`GraphDbEntityStore` communicates with the Ontotext GraphDB server over HTTP. Individual
`SaveAsync` and `DeleteAsync` calls each issue one or more SPARQL Update requests to the
`/repositories/{id}/statements` endpoint. GraphDB processes each request atomically at
the server, but successive requests are independent: a failure after the first request has
committed leaves the store in a partially-modified state.

To offer `ITransactionalEntityStore`, the implementation must apply all operations in an
all-or-nothing fashion, with server-side isolation from concurrent callers.

## Options

1. **Use the GraphDB REST Transactions API.**
   `POST /repositories/{id}/transactions` opens a server-managed transaction and returns
   a transaction URL (via `Location` header). Subsequent SPARQL Updates are sent to
   `{txUrl}/statements` (PUT), queries to `{txUrl}` (POST). A `PUT {txUrl}?action=COMMIT`
   commits; a `DELETE {txUrl}` rolls back. The server enforces isolation between concurrent
   transactions automatically.
2. Batch all operations into one SPARQL 1.1 Update multi-statement request.
   Pro: single HTTP round-trip. Con: fails silently on partial errors (SPARQL Update does
   not roll back earlier statements in the same request if a later one fails in GraphDB's
   default mode); no server-side isolation against concurrent callers.
3. Client-side locking only (like InMemory). Con: HTTP requests from unrelated processes
   are not subject to client locks. Provides no isolation at the server level.

## Decision

Option 1.

### Protocol

| Step | HTTP call |
|---|---|
| Open | `POST {BaseUrl}/repositories/{RepoId}/transactions` — `201 Created`, `Location: {txUrl}` |
| Query within tx | `POST {txUrl}` with SPARQL body, `Accept: application/sparql-results+json` |
| Update within tx | `PUT {txUrl}/statements` with SPARQL Update body, `Content-Type: application/sparql-update` |
| Commit | `PUT {txUrl}?action=COMMIT` |
| Rollback | `DELETE {txUrl}` (best-effort; original exception is re-thrown regardless) |

### Implementation

A new partial-class file `GraphDbTransactionalStore.cs` adds `ExecuteTransactionAsync`
to `GraphDbEntityStore`. The method:

1. Opens a transaction and captures the `txUrl`.
2. Iterates operations:
   - `DeleteOperation` → build `DELETE WHERE` + blank-node closure `DELETE … WHERE` SPARQL
     and `PUT {txUrl}/statements`.
   - `EntityWriteOperation` with `Mode = Create` → first run the ASK guard `POST {txUrl}`;
     if the IRI exists, rollback and throw `InvalidOperationException`. Then build
     `INSERT DATA` SPARQL and `PUT {txUrl}/statements`.
   - `EntityWriteOperation` with `Mode = Replace` → build `DELETE WHERE ; INSERT DATA`
     SPARQL and `PUT {txUrl}/statements`.
3. On success → `PUT {txUrl}?action=COMMIT`.
4. On any exception → `DELETE {txUrl}` (best-effort rollback), then rethrow.

Non-generic entity projection re-uses `ProjectEntity(IEntity, IRdfTripleSink, string)` on
`IRdfMapper` (introduced by Entity ADR-0015) and `IRdfMapperRegistry.ForEntityType(Type)`.

## Consequences

- Server-side ACID semantics: GraphDB's transaction manager guarantees isolation from
  concurrent writers.
- The transaction URL is per-call and not stored as instance state; concurrent
  `ExecuteTransactionAsync` calls on the same store object each open their own server
  transaction without client-side locking.
- `GraphDbOptions` is unchanged; the transaction endpoint is derived as
  `{BaseUrl}/repositories/{RepositoryId}/transactions`.
