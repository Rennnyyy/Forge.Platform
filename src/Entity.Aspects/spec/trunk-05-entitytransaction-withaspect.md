# Trunk 5 — `EntityTransaction.WithAspect(...)` per-call composition

- **Owner**: Operations agent (or shared)
- **Prerequisites**: Trunk 2 (engine + resolver)
- **ADRs**:
  - [Aspects ADR-0001](../../src/Entity.Aspects/adr/0001-split-shape-validation.md) §"Engine seam" — caller-supplied per-call shapes are merged by `IAspectResolver` for the duration of the transaction only.
  - [Operations ADR-0004](../../src/Entity.Operations/adr/0004-begin-transaction-ambient.md) — existing transaction entry-point.

## Goal

Add a fluent `WithAspect(...)` method to `EntityTransaction` so callers can compose
ad-hoc shape constraints into a single transaction without registering them in DI.

## Scope

- Extend `EntityTransaction` with `WithAspect(Aspect aspect)`.
- The Aspects resolver merges these per-call Aspects with Code-origin and
  Repository-origin Aspects **for the lifetime of that transaction only**. They do not
  leak into subsequent transactions.
- Tests for both happy path and isolation.
- No changes to the Aspects engine; the merge happens in `IAspectResolver` based on a
  per-transaction additional-shapes list.

## Deliverables

### `src/Entity.Repository/EntityTransaction.cs`

Add the chainable method:

```csharp
public EntityTransaction WithAspect(Aspect aspect)
{
    ThrowIfFinished();
    ArgumentNullException.ThrowIfNull(aspect);
    _additionalAspects.Add(aspect);
    return this;
}
```

`_additionalAspects` is an internal list exposed to the Aspects decorator via either:

- A new internal property on `EntityTransaction` (`internal IReadOnlyList<Aspect>
  AdditionalAspects { get; }`), accessed by `AspectEnforcingTransactionalStore` via
  `InternalsVisibleTo("Forge.Entity.Aspects")`; or
- A new optional parameter on `ITransactionalEntityStore.ExecuteTransactionAsync` (more
  intrusive — prefer `InternalsVisibleTo`).

Use the `InternalsVisibleTo` approach unless a strong reason emerges otherwise; it
keeps `ITransactionalEntityStore` unchanged and matches the `Forge.Entity` ↔
`Forge.Entity.Repository` reflection-contract precedent in Entity ADR-0013.

### `src/Entity.Aspects/`

Update `IAspectResolver`'s implementation to merge `EntityTransaction.AdditionalAspects`
into the active set. The merge happens at transaction begin (snapshot semantics from
Trunk 3 still apply).

### Tests — `tests/Entity.Aspects.Tests/`

1. **Per-call Aspect rejects an otherwise-valid operation**:
   - Without the per-call Aspect, the operation succeeds.
   - With `tx.WithAspect(stricter)`, the same operation fails and the transaction
     rolls back.
2. **Per-call Aspect does not leak**:
   - Run a transaction with `tx.WithAspect(stricter)` that fails.
   - Run a follow-up transaction (no `WithAspect`) with the same operation; it
     succeeds.
3. **Composition with Repository-origin Aspect**:
   - Register a Repository-origin Aspect plus an additional per-call Aspect; verify
     both are evaluated and either can fail the transaction.

## Acceptance criteria

- `dotnet test` passes.
- All three test cases above are covered.
- `EntityTransaction` exposes `WithAspect` as part of its public fluent surface.
- No backward-incompatible changes to `ITransactionalEntityStore` or
  `EntityTransaction.CommitAsync`.

## Out of scope

- Multi-call mutation API (`tx.WithAspects(IEnumerable<Aspect>)` etc.) — add later if
  needed.
- Persisting per-call Aspects (they are explicitly ephemeral).

## Suggested invocation

> `/Forge-Developer Implement Trunk 5 per spec/aspects-v1/trunk-05-entitytransaction-withaspect.md and Aspects ADR-0001.`
