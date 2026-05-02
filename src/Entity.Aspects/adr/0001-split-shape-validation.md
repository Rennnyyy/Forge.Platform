# 0001 — Split-shape validation: Local pass + Context pass

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

Aspect validation needs to handle two fundamentally different classes of constraint:

1. **Intra-entity** — "this entity, in isolation, satisfies the shape." Example: a required
   predicate is present, a literal value is within a permitted range.
2. **Cross-entity** — "this entity, in the context of the store's current state, satisfies
   the constraint." Example: a referenced entity exists, a uniqueness invariant holds.

Evaluating both classes the same way forces a trade-off: intra-entity checks become
unnecessarily expensive (querying the store for every property), and cross-entity checks
become impossible without store access.

SHACL natively separates these as node-shape constraints (evaluatable against a local
graph) and SPARQL-based constraints (`sh:sparql`, requires a queryable store).
dotNetRDF's `ShapesGraph.Validate(IGraph)` matches the local model exactly.

Per-operation validation ordering also matters. A transaction with `[Create(B), Create(A)]`
where B's context shape requires A fails if the engine sees B before A has been applied.
Reordering to `[Create(A), Create(B)]` must succeed. This must be an explicit,
documented contract — not an implementation accident.

## Options

1. **Two-pass model per operation: Local then Context, in queue order.**
   Local pass uses `ShapesGraph.Validate(IGraph)` against a single-subject projection.
   Context pass runs `sh:sparql` constraints via `ISparqlQueryStore.ExecuteSelectAsync`
   against the transaction-local state (live graph for InMemory; open transaction URL
   for GraphDB). Each operation is validated and then applied before moving to the next.
2. **Single pass with a full-graph projection.** Build the entire post-transaction graph
   and validate it at once. Simpler control flow. Con: cannot detect per-operation
   constraint violations mid-transaction; RI violations only appear after all writes;
   makes per-operation aspects (ADR-0003) semantically unsound.
3. **Delegate to GraphDB's native SHACL.** No engine needed. Con: InMemory has no SHACL.
   Cross-backend parity is lost. Rejected by ADR-0002.

## Decision

Option 1: two-pass model, per operation, in queue order.

### Validation pipeline (for each `TransactionOperation` in queue order)

1. Retrieve the declared `IAspect` from the operation (see ADR-0003).
2. If aspect is `Aspect.NoOp`, skip validation and apply the operation directly.
3. Cast to `IShapeAspect`. If the cast fails, throw `InvalidOperationException` — aspects
   that are not `IShapeAspect` and are not `Aspect.NoOp` are not supported in this version.
4. Assert the aspect is registered for `(entityType, operationKind)` via `IAspectResolver`.
   If not, throw `AspectNotRegisteredException` (fail-fast; not a SHACL violation).
5. **Local pass** — build a one-subject `Graph` from `IRdfMapper.ProjectEntity(entity, sink)`
   (Create / Update) or an empty graph (Delete). Evaluate `ShapesGraph.Validate(localGraph)`
   for the aspect's Local shape (if present). On `sh:Violation` severity → throw
   `AspectViolationException`; the surrounding `ExecuteTransactionAsync` rolls back.
6. **Context pass** — for each `sh:sparql` constraint in the aspect's Context shape (if
   present), run via `ISparqlQueryStore.ExecuteSelectAsync` against the transaction-local
   state. Empty SPARQL constraint list → pass is a no-op. On violation → throw as above.
7. **Apply** the operation; advance to the next operation.

### Per-operation ordering is the contract

Given a transaction `[Create(B), Create(A)]` where B's context SPARQL requires A to
exist: validation of B fires before A is applied → context pass fails → transaction rolls
back. This is correct and expected. The caller is responsible for ordering operations
such that dependencies are satisfied at the point each operation is validated.

### Severity threshold

Only `sh:Violation` throws. `sh:Warning` and `sh:Info` are silently ignored in v1.

## Consequences

- Trunk 3 (referential-integrity shapes) can add SPARQL-based context shapes without
  changing the engine — the pipeline already handles them.
- Queue ordering is a user-visible API contract; document clearly in `EntityTransaction`.
- The Local pass is purely synchronous (in-memory graph projection + SHACL eval);
  the Context pass is async (SPARQL query). `IAspectEngine.ValidateAsync` is always async.
