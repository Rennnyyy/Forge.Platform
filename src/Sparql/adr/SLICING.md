# SLICING — Forge.Sparql

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Sparql` | LINQ-to-SPARQL translation layer providing an `IQueryable<T>`-based API over `ISparqlQueryStore`. | All SPARQL query-building types live here. This slice is intentionally flat: `SparqlQueryable<T>` is the entry point; `LinqToSparqlVisitor` translates LINQ expression trees; `SparqlEmitter` serializes the query model; `SparqlQueryModel` is the IR; `EntityPredicateMap` and `EntitySparqlExtensions` are helpers. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Sparql`)

- `SparqlQueryable.cs` — `IQueryable<T>` implementation that defers to `LinqToSparqlVisitor` for query translation and `ISparqlQueryStore` for execution.
- `LinqToSparqlVisitor.cs` — expression-tree visitor that translates supported LINQ operators (`Where`, `Select`, `OrderBy`, `Take`, `Skip`) into a `SparqlQueryModel`.
- `SparqlQueryModel.cs` — intermediate representation of a SPARQL SELECT query (patterns, filters, ordering, pagination).
- `SparqlEmitter.cs` — serializes a `SparqlQueryModel` to a SPARQL 1.1 SELECT string.
- `EntityPredicateMap.cs` — reflects on an entity type to build the property→predicate-IRI mapping used during query translation.
- `EntitySparqlExtensions.cs` — `IQueryable<T>` extension methods providing the public LINQ-to-SPARQL entry point.
- `AsyncQueryableExtensions.cs` — async enumeration helpers (`ToListAsync`, `FirstOrDefaultAsync`, etc.) for `IQueryable<T>` backed by `ISparqlQueryStore`.
