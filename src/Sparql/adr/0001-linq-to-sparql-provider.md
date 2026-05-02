# 0001 — LINQ-to-SPARQL provider as the slice's public surface

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

Root ADR-0005 anticipated a future `Forge.Sparql` slice for query construction.
`Forge.Repository` ships only a coarse type-stream API (`QueryByTypeAsync<T>`) plus
opaque-IRI fetches. Application code that needs filtered, ordered, paged access has no
choice but to load everything and filter in memory. This both wastes bandwidth and breaks
the back-end's ability to honor security filters / federation rules.

The Operations slice already gives callers an EF-Core-shaped vocabulary
(`CreateAsync` / `ReadAsync` / `UpdateAsync` / `DeleteAsync` / `ListAsync`). The natural
next step is an EF-Core-shaped *query* surface: a typed `IQueryable<T>` that translates
LINQ expression trees into SPARQL.

## Options

1. **New slice `Forge.Sparql`** that ships a typed `IQueryable<T>` provider and a
   LINQ → SPARQL translator. Entry-points live on the existing ambient layer
   (`EntityOperations.Query<T>()`) and as an extension on `IEntityStore`. The slice owns
   the entire LINQ-provider stack (visitor, query model, emitter, async execution
   adapters); back-ends only need to implement `ISparqlQueryStore` (ADR-0002).
2. Add LINQ on top of `IEntityRepository<T>` directly. Pro: no new slice. Con: pulls a
   substantial amount of expression-tree machinery into the Repository slice and couples
   `IEntityStore` to a query model that not every back-end can answer.
3. Hand-author a fluent query DSL (e.g. `store.Where<Artist>().Eq(x => x.Country, "us")`).
   Pro: trivial to implement. Con: the user explicitly asked for an EF-Core-like
   experience; LINQ is the only ergonomically equivalent surface in .NET.

## Decision

Option 1.

### Slice layout

| File | Purpose |
|------|---------|
| `EntityPredicateMap.cs` | Per-type reflection scan: data-property → predicate-IRI map plus type-IRI resolver. Mirrors `ReflectionRdfMapper`'s plan but is read-only and reusable from the visitor. |
| `SparqlQueryModel.cs` | Intermediate representation: filter AST, ordering list, skip / take, terminal kind (`Entities`, `Count`, `Any`, `First`, `Single`). |
| `LinqToSparqlVisitor.cs` | `ExpressionVisitor` that consumes a LINQ method-call chain (`Where` → `OrderBy` → `Skip` → `Take` → terminal) and builds the `SparqlQueryModel`. |
| `SparqlEmitter.cs` | Model → SPARQL string. Emits `SELECT DISTINCT ?s` plus `OPTIONAL { ?s <pred> ?v }` for every filtered/ordered property, then `FILTER`, `ORDER BY`, `OFFSET`, `LIMIT`. Count-shape emits `SELECT (COUNT(DISTINCT ?s) AS ?c)`. |
| `SparqlQueryProvider.cs` | `IQueryProvider` implementation — non-terminal calls return a new `SparqlQueryable<T>`; terminal calls compile and execute. |
| `SparqlQueryable.cs` | `IOrderedQueryable<T>` + `IAsyncEnumerable<T>` — the user-facing handle. |
| `AsyncQueryableExtensions.cs` | EF-shaped async terminals: `ToListAsync`, `ToArrayAsync`, `CountAsync`, `LongCountAsync`, `AnyAsync`, `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`. |
| `EntitySparqlExtensions.cs` | Entry-points: `IEntityStore.Query<T>()` extension that requires the bound store to implement `ISparqlQueryStore`. |

### v1 LINQ surface

Supported:

- `Where(x => predicate)` with `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`, null
  comparisons, plus `string.Contains` / `StartsWith` / `EndsWith` (translated to
  `CONTAINS` / `STRSTARTS` / `STRENDS`). The left-hand side must be a `[Predicate]`
  scalar property of `T` or `T.Iri` (the subject).
- `OrderBy` / `OrderByDescending` / `ThenBy` / `ThenByDescending` keyed on a
  `[Predicate]` scalar property.
- `Skip(n)` → `OFFSET n`; `Take(n)` → `LIMIT n`.
- Terminal operators: `Count` / `LongCount`, `Any` (no-arg + predicate), `First` /
  `FirstOrDefault`, `Single` / `SingleOrDefault`, plus their async equivalents.
- Materialization shape is always full-entity (`IQueryable<T>` only; no `Select` to
  anonymous types in v1). `Select(x => x)` is accepted as a no-op.

Out of scope for v1 (deferred to a follow-up ADR + change):

- Projection to other shapes (`Select` to anonymous / record types).
- Joins across entity references (`x.PerformedBy.Name == "..."`).
- `GroupBy` / aggregates other than `Count`.
- Owning-collection traversal in filters.
- Server-side string transformations beyond the three above.

### Materialization

The translator produces a `SELECT DISTINCT ?s` query that returns subject IRIs. The
provider then calls `IEntityStore.LoadAsync<T>(iri)` per result row. This is an N+1
fetch in the worst case but acceptable for v1 — the alternative (a full `CONSTRUCT`
that ships the entire subject closure for every match) is a follow-up optimization
that does not change the LINQ surface.

`Count` / `Any` are evaluated server-side via dedicated SPARQL shapes; no entity is
materialized.

### Entry-points

Two equivalent entry-points are exposed by this slice:

- `store.Query<T>()` — extension on `IEntityStore` (requires `ISparqlQueryStore`).
- `EntityOperations.Query<T>()` — added to the Operations slice in
  [Entity.Operations ADR-0003](../../Entity.Operations/adr/0003-iqueryable-entry-point.md).
  Operations references this slice transitively for that entry-point.

## Consequences

- The Repository slice gains exactly one new public type (`ISparqlQueryStore`,
  ADR-0002). All other LINQ machinery is contained inside the Sparql slice.
- The `IEntityStore` contract is unchanged — back-ends without SPARQL support still
  compile and run; they simply throw `NotSupportedException` from `Query<T>()`.
- The slice surface is intentionally small and EF-Core-shaped; future LINQ features
  (joins, projections) extend the visitor / emitter without changing the public
  entry-point names.
- N+1 materialization is a known v1 tradeoff and a follow-up ADR will record the
  CONSTRUCT-based optimization when it is implemented.
