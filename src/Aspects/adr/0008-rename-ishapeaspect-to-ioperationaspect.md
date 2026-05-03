# 0008 — Rename `IShapeAspect` → `IOperationAspect`

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0006 renamed the thin identity token `IAspect` to `IOperationAspect` to resolve
ambiguity once `IQueryAspect` was introduced as a second aspect family. That rename was
correct but incomplete: the concrete write-validation interface `IShapeAspect` survived,
leaving a naming inconsistency across the three aspect families now present in the slice:

| Interface | Lives in | Describes |
|---|---|---|
| `IOperationAspect` | `Forge.Repository` | Thin identity token (name only) |
| `IShapeAspect : IOperationAspect` | `Forge.Aspects` | CUD write validation (Local SHACL + Context SPARQL) |
| `IQueryAspect : IOperationAspect` | `Forge.Aspects` | Read validation (filter gate + result SHACL) |
| `IMessageAspect : IOperationAspect` | `Forge.Capability` | Message validation (input/output SHACL) |

The name `IShapeAspect` bleeds implementation detail ("shape") into the interface name.
`IQueryAspect` and `IMessageAspect` do not carry their implementation strategy in their
name; `IShapeAspect` should not either. The consistent pattern is:

> `I<Scope>Aspect` — where scope names the domain context, not the validation mechanism.

For writes, the scope is the `TransactionOperation` — hence the name `IOperationAspect`
was already taken by the base token. The correct scope name for CUD write aspects is the
operation kind they validate: **write**.

## Options

1. **Rename `IShapeAspect` → `IWriteAspect`.**
   Names the domain scope (`Write`). Consistent with `IQueryAspect` and `IMessageAspect`.
   `LocalShapeTtl` and `ContextWhere` remain as-is — the implementation strategy is still
   expressed in the property names, not the interface name.
2. **Keep `IShapeAspect`.** No churn. Con: worsening inconsistency as the aspect family
   grows; new contributors will not understand why one interface is named after its
   mechanism.
3. **Rename to `ICudAspect`.** Precise about the operation types. Con: `CUD` is jargon;
   `Write` is universally understood and maps naturally to `IWriteAspect`.
4. **Rename to `ITransactionAspect`.** Con: `ITransaction` is overloaded in the
   `Forge.Repository` vocabulary (`ITransactionalEntityStore`, `EntityTransaction`);
   the name suggests it applies to a whole transaction rather than an individual operation
   within one.

## Decision

Option 1 — rename `IShapeAspect` to `IWriteAspect`.

### Changes

| Location | Before | After |
|---|---|---|
| `Forge.Aspects/IShapeAspect.cs` | `public interface IShapeAspect` | `public interface IWriteAspect` |
| `Forge.Aspects/IAspectResolver.cs` | `IShapeAspect Resolve(…)` | `IWriteAspect Resolve(…)` |
| `Forge.Aspects/IShapeRegistry.cs` | `IShapeAspect? TryGet(…)` | `IWriteAspect? TryGet(…)` |
| `Forge.Aspects/ShapeRegistry.cs` | all `IShapeAspect` references | `IWriteAspect` |
| `Forge.Aspects/AspectEngine.cs` | cast to `IShapeAspect` | cast to `IWriteAspect` |
| `Forge.Aspects/InlineTtlShapeAspect.cs` | `: IShapeAspect` | `: IWriteAspect` |
| File renamed | `IShapeAspect.cs` | `IWriteAspect.cs` |
| `Forge.Aspects.adr/README.md` | refers to `IShapeAspect` in ADR-0001 summary | refers to `IWriteAspect` |

`LocalShapeTtl` and `ContextWhere` property names are **not** changed — they correctly
describe the validation mechanism within the interface and are not subject to this rename.

### Parallel to the base-token rename (ADR-0006)

The two renames are complementary:

| ADR | Before | After | Reason |
|---|---|---|---|
| 0006 | `IAspect` | `IOperationAspect` | Disambiguate base token from query leg |
| 0008 | `IShapeAspect` | `IWriteAspect` | Align scope-naming convention across all legs |

## Consequences

- The three concrete aspect interfaces now follow a uniform `I<Scope>Aspect` pattern:
  `IWriteAspect`, `IQueryAspect`, `IMessageAspect`.
- `InlineTtlShapeAspect` is renamed to `InlineTtlWriteAspect` for filesystem

> *`IWriteAspect` subsequently renamed to `IOperationAspect` due to Aspects ADR-0009. The decision to scope the write-validation interface by domain context (not mechanism) is unchanged.*
  consistency; the constructor signature is otherwise unchanged.
- All registration call sites that reference `IShapeAspect` or `InlineTtlShapeAspect`
  must be updated — no other behavioral changes.
- Tests and snapshot baselines that use `IShapeAspect` or `InlineTtlShapeAspect`
  by name must be updated.
