# 0002 — `ISparqlQueryStore` is the backend seam, lives in Entity.Repository

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

The LINQ-to-SPARQL provider (ADR-0001) needs a way to push a compiled SPARQL string at
the back-end and receive back a result-set of bindings. Every back-end already speaks
SPARQL natively — `InMemoryEntityStore` via dotNetRDF's Leviathan processor, the
GraphDB back-end via the SPARQL HTTP endpoint. The seam should be small enough that an
existing `IEntityStore` implementation can opt in with one method, and it should not
force back-ends that do not support SPARQL to take a dependency on the Sparql slice.

## Options

1. **Place `ISparqlQueryStore` in `Forge.Repository`** as a sibling interface to
   `IEntityStore`. Back-ends implement both on the same concrete class. The Sparql slice
   only consumes the interface; back-ends never reference the Sparql slice. The interface
   exposes a single async-stream method returning bindings as a uniform shape.
2. Place `ISparqlQueryStore` in `Forge.Sparql` and have each back-end take a
   `ProjectReference` on it. Pro: keeps Repository unaware of SPARQL. Con: every back-end
   project gains a transitive dependency on the LINQ-provider slice; back-ends that
   never need LINQ still pay for the reference.
3. Bake the SPARQL execution method into `IEntityStore` directly. Pro: zero new
   interfaces. Con: forces every back-end (including non-SPARQL future ones) to either
   throw or implement; pollutes the core store contract; conflicts with ADR-0005's
   stance that `IEntityStore` is the small, type-agnostic seam.

## Decision

Option 1.

### Interface

```csharp
namespace Forge.Repository;

public interface ISparqlQueryStore
{
    IAsyncEnumerable<SparqlResultRow> ExecuteSelectAsync(
        string sparql, CancellationToken cancellationToken = default);
}

public sealed record SparqlResultRow(IReadOnlyDictionary<string, RdfTerm> Bindings);
```

`SparqlResultRow` carries the variable → `RdfTerm` mapping for one solution. Using
`RdfTerm` (already defined in `Forge.Repository.Rdf`) keeps the shape uniform
with the rest of the slice and lets consumers tell IRIs apart from literals without
back-end-specific types.

### Backend opt-in

- `InMemoryEntityStore` implements `ISparqlQueryStore` by parsing the SPARQL string with
  `SparqlQueryParser` and running it through `LeviathanQueryProcessor` over an
  `InMemoryDataset` of its underlying graph.
- Future back-ends (e.g. GraphDB) opt in independently; the Sparql slice does not care
  which back-end is bound.

### Discovery

`Forge.Sparql.EntitySparqlExtensions.Query<T>()` requires the ambient or passed
`IEntityStore` to implement `ISparqlQueryStore`. If it does not, the call throws
`NotSupportedException` with a message naming the actual store type — the entry-point
fails fast and predictably.

## Consequences

- The Repository slice gains one new public interface and one new public record
  (`SparqlResultRow`). No existing types change.
- Back-ends that cannot or do not want to expose SPARQL simply omit the implementation.
- The Sparql slice depends only on `Forge.Repository`, not on any specific
  back-end.
- A future GraphDB SPARQL implementation is a one-method addition on
  `GraphDbEntityStore`; no churn elsewhere.
