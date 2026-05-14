# 0004 — Entity-level graph diff engine (`IBranchDiffEngine`)

- **Status**: accepted
- **Date**: 2026-05-14
- **Author**: agent

## Context

Applying a branch merge requires first knowing *what actually differs* between the source
named graph and the target named graph. Raw triple-level differences are insufficiently
structured for the merge pipeline: the planner (ADR-0006) and merge service (ADR-0007)
need to reason about whole **entities**, not individual `<s, p, o>` rows.

Design decisions carried in:

- Diff is **directional**: source → target. Only source-side changes matter; entities
  present only in the target are intentionally preserved (upsert policy).
- The diff covers **all** triples in the source entity's closure, including `rdf:type`
  triples. A type change on the source side is a data mutation and must be propagated.
- Diff computation uses **SPARQL `MINUS`** at the store level when the backend supports
  `ISparqlQueryStore`; InMemory backends fall back to materialising both triple sets and
  set-diffing in process.
- There is **no conflict detection**. The engine produces the delta and the merge service
  applies it; target-side mutations since the common ancestor are silently overwritten.

## Options

### Option A — SPARQL-only `IBranchDiffEngine`

Execute a single query against the SPARQL endpoint that returns differing entity IRIs and
their most-derived `rdf:type`. Cast `IEntityStore` to `ISparqlQueryStore` at runtime; throw
`NotSupportedException` on backends that do not expose SPARQL.

Con: InMemory is excluded. InMemory is the primary testing backend; excluding it makes the
merge pipeline untestable without a running GraphDB instance.

### Option B — Dual-path `IBranchDiffEngine` with optional SPARQL

Single interface. The implementation checks `ISparqlQueryStore` via pattern-matching on
the injected store. When available, delegates to a SPARQL `SELECT` query. When absent,
loads both graphs eagerly via `QueryByTypeAsync<T>` for all registered mappers and diffs the
triple projections in memory.

Pro: works with both backends; no extra surface area on `ISparqlQueryStore`; the
implementation detail of which path is taken is hidden from callers.
Con: in-memory path requires iterating all registered mapper types to `QueryByTypeAsync`,
which is exhaustive. Acceptable for testing; not a concern in production.

### Option C — Separate engine types registered by backend key

`InMemoryBranchDiffEngine` and `GraphDbBranchDiffEngine` registered under the store key.
Con: The `Forge.Branch` slice would need to depend on both `Forge.Repository.InMemory` and
`Forge.Repository.GraphDb`; violates the slice isolation principle (ADR-0008).

## Decision

**Option B.**

### `IBranchDiffEngine`

Defined in `Forge.Branch`:

```csharp
public interface IBranchDiffEngine
{
    /// <summary>
    /// Computes the directional entity-level diff between two named graphs.
    /// Returns entries for entities that exist in <paramref name="sourceGraphIri"/> and
    /// are either absent from or different in <paramref name="targetGraphIri"/>.
    /// Entities present only in the target are not included (upsert policy).
    /// </summary>
    Task<EntityGraphDelta> ComputeDiffAsync(
        string sourceGraphIri,
        string targetGraphIri,
        CancellationToken cancellationToken = default);
}
```

### Carrier types

```csharp
public enum EntityDeltaKind { Added, Modified }

public sealed record EntityDeltaEntry(
    string EntityIri,
    string TypeIri,         // most-derived rdf:type IRI from the source graph
    EntityDeltaKind Kind);

public sealed record EntityGraphDelta(
    string SourceGraphIri,
    string TargetGraphIri,
    IReadOnlyList<EntityDeltaEntry> Entries)
{
    public bool IsEmpty => Entries.Count == 0;
}
```

`TypeIri` carries the most-derived `rdf:type` IRI for the entity in the source graph. The
merge planner (ADR-0006) passes it to `IRdfMapperRegistry.ForTypeIri(...)` to resolve the
CLR type for hydration.

### SPARQL path (GraphDB and any `ISparqlQueryStore` backend)

Two queries are issued:

**Query 1 — Added entities** (exist in source, absent from target entirely):
```sparql
SELECT DISTINCT ?entity ?type WHERE {
  GRAPH <{sourceGraph}> { ?entity a ?type }
  FILTER NOT EXISTS { GRAPH <{targetGraph}> { ?entity ?p ?o } }
}
```

**Query 2 — Modified entities** (exist in both graphs but have at least one differing triple):
```sparql
SELECT DISTINCT ?entity ?type WHERE {
  GRAPH <{sourceGraph}> { ?entity a ?type }
  FILTER EXISTS      { GRAPH <{targetGraph}> { ?entity ?p ?o } }
  FILTER EXISTS {
    { GRAPH <{sourceGraph}> { ?entity ?p ?o } FILTER NOT EXISTS { GRAPH <{targetGraph}> { ?entity ?p ?o } } }
    UNION
    { GRAPH <{targetGraph}> { ?entity ?p ?o } FILTER NOT EXISTS { GRAPH <{sourceGraph}> { ?entity ?p ?o } } }
  }
}
```

Results from Query 1 → `EntityDeltaKind.Added`.
Results from Query 2 → `EntityDeltaKind.Modified`.
Results are deduplicated by entity IRI; `TypeIri` from each row is taken as-is.

### InMemory path (no `ISparqlQueryStore`)

The implementation iterates `IRdfMapperRegistry.All` and for each mapper issues
`QueryByTypeAsync<T>` against both graphs (scoped via `BranchScope.Use(...)` on the
store). An entity is `Added` if its IRI is absent from the target; `Modified` if it is
present but its projected triple-set differs when both entities are projected into
`ListRdfTripleSink` buffers and compared as sets.

### `BranchDiffEngine` registration

`AddForgeBranch()` registers `BranchDiffEngine` as the `IBranchDiffEngine` singleton.
The engine receives the unkeyed `IEntityStore` (the full decorator chain including auth
and aspects). It casts to `ISparqlQueryStore` at compute time via `as`; no DI changes to
`IEntityStore` contracts.

## Consequences

- The SPARQL path scales to large graphs without materialising entity instances.
- The InMemory path provides full merge-pipeline coverage in unit and integration tests
  without a running GraphDB.
- `IBranchDiffEngine` is the only new public surface area; carrier types are value objects
  with no dependencies.
- Type resolution via `IRdfMapperRegistry.ForTypeIri` requires that all entity types
  participating in a merge are registered at DI time. Unregistered types will produce a
  `null` mapper and should be surfaced as a descriptive exception by the planner.
