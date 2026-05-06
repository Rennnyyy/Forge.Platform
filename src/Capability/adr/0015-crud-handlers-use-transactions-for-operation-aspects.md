# 0015 — Generated CUD handlers route through `EntityTransaction` for operation-aspect support

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

ADR-0012 specified that generated `CreateXHandler`, `UpdateXHandler`, `DeleteXHandler`
classes delegate to the entity's active-record methods (`entity.CreateAsync()`,
`entity.UpdateAsync()`, `entity.DeleteAsync()`). These methods call
`EntityOperations.RequireStore().SaveAsync(…)` / `.DeleteAsync(…)` which routes to the
ambient `IEntityStore`.

In a Forge application that calls `AddForgeAspects()`, the ambient `IEntityStore` is the
`AspectEnforcingEntityStore` decorator, which handles *read-aspect* validation. Write-
aspect (`IOperationAspect`) validation is performed by `AspectEnforcingTransactionalStore`
— a separate DI registration on `ITransactionalEntityStore`. Because the active-record
write methods never open a transaction, `IOperationAspect` is unreachable from any
generated CUD handler.

Aspects ADR-0010 adds `CapabilityAspect.OperationAspectIri`. This ADR decides how the
generated handlers consume it.

## Options

1. **Inject `ITransactionalEntityStore`; always use `EntityTransaction` for
   Create/Update/Delete.** Pass `context.Aspect?.OperationAspectIri ?? Aspect.NoOpIri`
   as the aspect IRI. `AspectEnforcingTransactionalStore` fast-paths `NoOpIri` operations,
   so the overhead when no operation aspect is registered is negligible.
   Pro: one code path; the transaction wrapping is always present and formally correct.
   Con: Create/Update/Delete handlers now depend on `ITransactionalEntityStore` even
   when no aspect is configured.

2. **Conditional branching: use active-record path when no `OperationAspectIri`, use
   `EntityTransaction` otherwise.** Con: two code paths in every generated handler;
   branch coverage requirement; more generator complexity; ALREADY_EXISTS handling
   duplicated.

3. **New ambient `OperationAspectScope` (analogous to `QueryAspectScope`).** A static
   `AsyncLocal` set by the HTTP layer before dispatch. The generator emits no transaction
   code; the decorator picks it up automatically.
   Con: the generator is already aware of `CapabilityContext` for future use; ambient-
   scope coupling is harder to reason about than explicit parameter threading; breaks the
   "caller-declared aspect per operation" invariant (Aspects ADR-0003).

## Decision

Option 1.

### Generator changes

For `Create`, `Update`, `Delete` handlers only (Read and List are unaffected):

**Constructor injection** — each CUD handler receives `ITransactionalEntityStore` as a
constructor parameter:
```csharp
private readonly global::Forge.Repository.Transaction.ITransactionalEntityStore _txStore;

public CreateXHandler(global::Forge.Repository.Transaction.ITransactionalEntityStore txStore)
{
    _txStore = txStore;
}
```

**Entity transaction** — the direct active-record call is replaced:

*Create (was `entity.CreateAsync(ct)` inside a try/catch):*
```csharp
var aspectIri = context.Aspect?.OperationAspectIri
    ?? global::Forge.Aspects.Aspect.NoOpIri;
try
{
    await using var tx = new global::Forge.Repository.Transaction.EntityTransaction(_txStore);
    tx.Create(entity, aspectIri);
    await tx.CommitAsync(cancellationToken);
}
catch (global::System.InvalidOperationException ex)
{
    return new CapabilityResult<CreateXResponse>.Fail(
        new CapabilityError("ALREADY_EXISTS", ex.Message));
}
```

*Update (was `entity.UpdateAsync(ct)` after prop assignment):*
```csharp
var aspectIri = context.Aspect?.OperationAspectIri
    ?? global::Forge.Aspects.Aspect.NoOpIri;
await using var tx = new global::Forge.Repository.Transaction.EntityTransaction(_txStore);
tx.Update(entity, aspectIri);
await tx.CommitAsync(cancellationToken);
```

*Delete (was `entity.DeleteAsync(ct)` after not-found guard):*
```csharp
var aspectIri = context.Aspect?.OperationAspectIri
    ?? global::Forge.Aspects.Aspect.NoOpIri;
await using var tx = new global::Forge.Repository.Transaction.EntityTransaction(_txStore);
tx.Delete(command.Iri, aspectIri);
await tx.CommitAsync(cancellationToken);
```

### ALREADY_EXISTS semantics

`InvalidOperationException` on duplicate Create was previously thrown by the inner store
from `SaveAsync`. It is now thrown from `tx.CommitAsync()` via the same inner store.
The catch and error-code mapping remain identical.

### `AspectViolationException` surfacing

`AspectEnforcingTransactionalStore.ExecuteTransactionAsync` throws
`AspectViolationException` when a Local or Context constraint fires. This exception is
**not** caught inside the generated handler — it propagates up through
`CapabilityDispatcher.DispatchAsync` to the HTTP layer. See Capability.Http ADR-0008 for
the 422 mapping.

### DI compatibility

`ITransactionalEntityStore` is registered (as `AspectEnforcingTransactionalStore`) by
`AddForgeAspects()`. The handler is registered as `Transient` by
`AddCapabilityHandlers`; DI resolves the constructor automatically.

## Consequences

- Generated CUD handlers always produce formally correct transactional writes, even
  without an operation aspect. The minor overhead of wrapping a `NoOp` write in an
  `EntityTransaction` is acceptable.
- `IOperationAspect` constraints are reachable end-to-end from a capability dispatch
  call for the first time.
- Read and List handlers are unaffected.
- Snapshot test assertions for Create/Update/Delete generated bodies must be updated.
