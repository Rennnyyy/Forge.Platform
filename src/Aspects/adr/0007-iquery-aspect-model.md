# 0007 — `IQueryAspect`: filter injection + result-graph SHACL for reads and queries

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

The CUD aspect model (ADRs 0001–0005) enforces constraints *before* a write is applied.
There is no equivalent mechanism for reads: a caller can `LoadAsync<T>` or execute a
SPARQL query with no access gate, no output shape contract, and no structured violation
signal. This gap becomes significant once multi-tenant data, per-user visibility rules,
or output-integrity guarantees are required.

The CUD model's two-pass design (WHERE-body Context pass + SHACL Local pass) maps
cleanly onto the read/query problem — with polarity adjustments:

| CUD | Read / Query |
|---|---|
| Context pass: rows returned = violation | Access gate: rows returned = access granted (inverted) |
| Local pass: SHACL on entity *being written* | Result-shape pass: SHACL on entity or graph *being returned* |

## Options

1. **`IQueryAspect : IOperationAspect` with `FilterWhere`, `ResultConstruct`, and
   `ResultShapeTtl`.** Symmetrical with CUD; reuses `ISparqlQueryStore`,
   `IShapeCache`, `IRdfMapper` infrastructure; three injection paths (LINQ/type-scan:
   append-only; point-load: pre-load gate query; dynamic SPARQL: placeholder
   substitution). Violation is always observable (`QueryAspectViolationException`).
2. **Post-load `IShapeAspect` validation only.** Validate the entity after load using
   the existing `IShapeAspect` SHACL path. Con: no access gate at the SPARQL level;
   does not compose with LINQ or dynamic queries; requires loading data before deciding
   to deny.
3. **Middleware / policy framework outside the Aspects engine.** Con: no integration with
   `EntitySession` lazy-load, LINQ provider, or InMemory; loses cross-backend parity.

## Decision

Option 1.

---

## `IQueryAspect` contract

```csharp
public interface IQueryAspect : IAspect  // IAspect = Forge.Repository.IAspect; see Aspects ADR-0009
{
    // Layer 1 — Access gate / filter.
    // SPARQL WHERE body fragment appended (generated queries) or substituted via
    // ##aspect:filter## placeholder (expert-authored dynamic SPARQL).
    // Null = no access filter applied.
    string? FilterWhere { get; }

    // Layer 2a — Result graph construction.
    // Full SPARQL CONSTRUCT statement (not just WHERE body) to build the result graph
    // for SHACL validation. Must contain ##aspect:filter## if FilterWhere is non-null.
    // Null = use IRdfMapper.ProjectEntity (typed entity reads/scans only).
    string? ResultConstruct { get; }

    // Layer 2b — Result graph validation.
    // Turtle-serialized SHACL shape. Validated once against the aggregate result graph
    // (all projected/constructed triples in the full result set, not per-row).
    // Null = no output shape check.
    string? ResultShapeTtl { get; }
}
```

---

## Pipeline per call type

### Point read — `LoadAsync<T>(iri)`

1. **Access gate** (if `FilterWhere` non-null)
   Build and execute:
   ```sparql
   SELECT ?granted WHERE {
     VALUES ?entityIri { <iri> }
     BIND(true AS ?granted)
     <FilterWhere body>
   }
   ```
   Zero rows → throw `QueryAspectViolationException` (observable; never silent null).
2. **Load** via `_inner.LoadAsync<T>(iri)`.
3. **Result-shape pass** (if `ResultShapeTtl` non-null)
   Project the loaded entity via `IRdfMapper.ProjectEntity` into a single-subject
   `Graph`. Validate `ShapesGraph.Validate(resultGraph)` using `IShapeCache`.
   Any violation → throw `QueryAspectViolationException`.

`ResultConstruct` is **ignored** for point reads — `IRdfMapper` is always preferred
when a typed entity is available. `ResultConstruct` is only used for dynamic SPARQL.

### Collection scan — `QueryByTypeAsync<T>`

1. **Filter injection**: engine appends `FilterWhere` to the generated type-scan
   SPARQL WHERE block before execution. No round-trip overhead.
2. **Result-shape pass** (if `ResultShapeTtl` non-null):
   Accumulate all projected entity graphs into one aggregate `Graph`; call
   `ShapesGraph.Validate` once on the aggregate. Any violation → throw.

### LINQ — `store.Query<T>(queryAspect)`

Same as collection scan. `FilterWhere` is appended by `SparqlQueryProvider<T>` before
the SPARQL string is sent to the backend. `ResultShapeTtl` validated once on the
aggregate of all materialized entities.

### Dynamic SPARQL — expert-authored query strings

The query string **must** contain the placeholder token `##aspect:filter##` anywhere
inside a `WHERE { }` block when the active `IQueryAspect` has a non-null `FilterWhere`.

Engine behaviour:
```csharp
var injected = FilterWhere ?? string.Empty;
var finalQuery = queryString.Replace("##aspect:filter##", injected);
```

If `FilterWhere` is non-null and the query string lacks `##aspect:filter##` → throw
`QueryAspectViolationException` at query execution time (not at registration, because
query strings are runtime values). The exception message states the missing placeholder
explicitly.

If `ResultConstruct` is non-null: execute it (with the same placeholder substitution
applied) to build the result graph, then validate with `ResultShapeTtl`.

---

## Violation semantics

Access-gate or result-shape failure always throws `QueryAspectViolationException`.
Returns are never silently mutated to `null`. This mirrors the CUD side
(`AspectViolationException` is always thrown on violation).

Callers who want silent filtering must catch `QueryAspectViolationException` explicitly.
A convenience overload (`.TryQuery(aspect)` / `.TryLoadAsync`) may be added in a
future trunk.

---

## Scope

### What lives in `Forge.Repository`

Nothing new. `IOperationAspect` (the base) is already there after ADR-0006.

### What lives in `Forge.Aspects`

- `IQueryAspect : IOperationAspect` — the contract.
- `InlineTtlQueryAspect` — concrete code-origin implementation, parallel to `InlineTtlShapeAspect`.
- `QueryAspectViolationException` — thrown on access-gate or result-shape failure.
- `IQueryAspectEngine` — validates a single read/query call.
- `QueryAspectEngine` — implements `IQueryAspectEngine` using `ISparqlQueryStore` and `IShapeCache`.
- `AspectEnforcingEntityStore` — decorator over `IEntityStore`; intercepts `LoadAsync`,
  `QueryByTypeAsync`; delegates to `QueryAspectEngine`.
- `AspectKind.Read = 8` — added to the flags enum so aspects can be registered for
  read operations via `IShapeRegistry` if needed.

### Scope / ambient binding

`IQueryAspect` is set as an independent ambient scope via
`EntityOperations.UseReadAspect(IQueryAspect)`. This is independent of the write-side
`EntityTransaction` aspect on each `TransactionOperation`. A write transaction carrying
aspect A on its ops does **not** implicitly apply A to reads inside the same scope.

```csharp
using var _ = EntityOperations.UseReadAspect(ownerGate);
var artist = await Artist.ReadAsync(iri);         // gate applied
await foreach (var a in Artist.ListAsync()) { }   // gate applied
```

### Result graph granularity

`ResultShapeTtl` is validated **once** on the **aggregate** result graph — all entity
triples projected during the call, merged into one `Graph`. This means:
- For point reads: the aggregate has one subject (degenerate case, identical to CUD Local pass).
- For scans/LINQ: all subjects in the result set form one graph; SHACL can enforce
  cross-result invariants (e.g. uniqueness, completeness).

### Focus variable

`?entityIri` is pre-bound for the point-read access gate. For injection into
LINQ/scan-generated queries, the variable binding is handled implicitly by appending
the fragment. For dynamic SPARQL the aspect author controls all variable names via the
placeholder.

---

## Consequences

- The Aspects engine remains the sole SHACL evaluator (no native GraphDB SHACL — ADR-0002 holds).
- `ISparqlQueryStore` is required for any query aspect with `FilterWhere`; stores that
  do not implement it throw `NotSupportedException` (same contract as `Sparql`).
- `IShapeCache` is shared between write-side and read-side validation; the aggregate
  result graph reuses the same parse-and-cache path.
- The InMemory backend natively supports SPARQL via dotNetRDF; no backend disparity
  for `FilterWhere` or `ResultConstruct`.
- `ResultConstruct` for read scans is deferred to a future trunk; the current
  implementation uses `IRdfMapper` projection for typed queries.
