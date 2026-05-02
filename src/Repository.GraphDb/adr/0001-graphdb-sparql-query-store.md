# 0001 — `GraphDbEntityStore` implements `ISparqlQueryStore`

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`Sparql` ADR-0002 introduced `ISparqlQueryStore` as the opt-in seam that lets a
back-end accept opaque SPARQL SELECT strings. The ADR explicitly anticipated this as
_"a one-method addition on `GraphDbEntityStore`; no churn elsewhere"_. The
`InMemoryEntityStore` already implements the interface via its dotNetRDF Leviathan
processor. `GraphDbEntityStore` must now offer the same capability so that the
LINQ-to-SPARQL provider and any raw SPARQL consumer can work against a live Ontotext
GraphDB instance.

## Options

1. **Add `ExecuteSelectAsync` to `GraphDbEntityStore` by introducing a new partial-class
   file `GraphDbSparqlQueryStore.cs`.**
   The method POSTs the SPARQL string to `GraphDbOptions.QueryEndpoint` with
   `Content-Type: application/sparql-query`, accepts `application/sparql-results+json`,
   and streams the result rows as `SparqlResultRow` values. One private helper
   `JsonBindingToTerm` converts each SPARQL 1.1 binding element (`uri`, `bnode`,
   `literal`) to the `RdfTerm` value type already used throughout the slice.
   A partial-class split mirrors the `InMemoryEntityStore` / `InMemorySparqlQueryStore`
   precedent and keeps the SPARQL concern clearly delineated from the CRUD concern.
2. Inline the method at the bottom of `GraphDbEntityStore.cs`.
   Pro: one fewer file. Con: breaks the visual parallelism with the in-memory backend
   and mixes two distinct concerns (CRUD persistence vs query execution) in one file.
3. Introduce a separate `GraphDbSparqlQueryStore` class (not partial, not the same type).
   Pro: fully independent type. Con: requires callers to cast or resolve two objects from
   DI instead of one; the `EntitySparqlExtensions.Query<T>()` entry-point already
   expects the same `IEntityStore` reference to implement `ISparqlQueryStore`.

## Decision

Option 1.

### HTTP protocol

|Concern|Detail|
|---|---|
|Endpoint|`GraphDbOptions.QueryEndpoint` (`{BaseUrl}/repositories/{RepositoryId}`)|
|Request method|`POST`|
|Request content type|`application/sparql-query`|
|Accept header|`application/sparql-results+json`|
|Cancellation|`HttpCompletionOption.ResponseContentRead` + `CancellationToken` propagated throughout|

### Binding conversion

SPARQL 1.1 JSON binding types map to `RdfTerm` as follows:

| JSON `type` | `RdfTerm` factory |
|---|---|
| `"uri"` | `RdfTerm.Iri(value)` |
| `"bnode"` | `RdfTerm.Blank(value)` |
| `"literal"` | `RdfTerm.Literal(value, datatype?, xml:lang?)` |

Unknown types are silently dropped (variable absent in the result row), matching the
contract that `SparqlResultRow` unbound variables are absent from the `Bindings` map.

### Integration tests

A new test class `GraphDbSparqlQueryTests` in `Repository.GraphDb.Tests` mirrors
the behavioral coverage of `LinqToSparqlBehavioralTests` (in-memory) against a live
GraphDB instance managed by `GraphDbFixture` (Podman / Docker — see Entity ADR-0014).
Tests include:

1. Raw `ExecuteSelectAsync` with a hand-authored count query — verifies the full HTTP
   round-trip and binding deserialization independently of the LINQ layer.
2. LINQ provider integration (where, boolean, numeric, string, null, `OrderBy`/`Take`,
   `CountAsync`, `AnyAsync`, `FirstOrDefaultAsync`, `Skip`/`Take`) — verifies the full
   chain from LINQ expression through SPARQL emission to GraphDB execution and entity
   materialization.

All tests skip gracefully when `GraphDbFixture.Available` is `false`.

## Consequences

- `GraphDbEntityStore` now satisfies `ISparqlQueryStore`; `store.Query<T>()` works
  against GraphDB with no further change.
- `Repository.GraphDb.Tests` gains a `ProjectReference` to `Sparql` to
  access the LINQ-provider entry-points in tests.
- The `RdfTerm` and `SparqlResultRow` types are the only shared types between the
  Sparql slice and this back-end; there is no reverse dependency.
- `GraphDbEntityStore` is promoted from `sealed class` to `sealed partial class` to
  accommodate the split; this is a source-compatible change.
