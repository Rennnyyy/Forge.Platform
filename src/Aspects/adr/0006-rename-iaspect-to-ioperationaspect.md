# 0006 — Rename `IAspect` → `IOperationAspect`

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

`IAspect` (defined in `Forge.Repository`) is the thin token placed on each
`TransactionOperation` to declare which validation policy applies to a CUD write.
ADR-0007 introduces `IQueryAspect` — a parallel token for read / query operations.

With two distinct aspect families (`IOperationAspect` for CUD, `IQueryAspect` for
reads), the original name `IAspect` has become ambiguous: a reader cannot tell at a
glance whether an `IAspect` participates in write validation, read filtering, or both.
The name also collides conceptually with the `IQueryAspect` name at the documentation
level.

## Options

1. **Rename `IAspect` → `IOperationAspect`.** The name is precise: it marks a policy
   that applies to a `TransactionOperation`. `IQueryAspect` then has a clear sister
   name. `Aspect.NoOp` remains as-is (it is the no-op sentinel shared by both
   operation-level opt-out and the existing CUD path). Both `IShapeAspect` and
   `IQueryAspect` extend `IOperationAspect` for the `Name` property.
2. **Keep `IAspect`; add `IQueryAspect : IAspect`.** Avoids a rename churn. Con:
   `IQueryAspect` inheriting from something named `IAspect` is actively confusing —
   queries are not operations. Naming leaks the implementation history rather than the
   intent.
3. **Introduce a common `IAspectToken` base; `IOperationAspect : IAspectToken`;
   `IQueryAspect : IAspectToken`.** More symmetric, but introduces a third interface
   for a single `Name` property. Over-engineered for a v1 surface.

## Decision

Option 1.

### Changes

| Location | Before | After |
|---|---|---|
| `Forge.Repository/IAspect.cs` | `public interface IAspect` | `public interface IOperationAspect` |
| `Forge.Repository/Aspect.cs` | `public static readonly IAspect NoOp` | `public static readonly IOperationAspect NoOp` |
| `Forge.Repository/Aspect.cs` | `internal sealed class NoOpAspect : IAspect` | `internal sealed class NoOpAspect : IOperationAspect` |
| `Forge.Repository/TransactionOperation.cs` | `public IAspect Aspect` | `public IOperationAspect Aspect` |
| `Forge.Repository/EntityTransaction.cs` | `IAspect aspect` params | `IOperationAspect aspect` params |
| `Forge.Aspects/IShapeAspect.cs` | `: Forge.Repository.IAspect` | `: Forge.Repository.IOperationAspect` |
| `Forge.Aspects/IAspectResolver.cs` | `Resolve(IAspect …)` | `Resolve(IOperationAspect …)` |
| `Forge.Aspects/IShapeRegistry.cs` | `TryGet(IAspect …)` | `TryGet(IOperationAspect …)` |
| `Forge.Aspects/ShapeRegistry.cs` | all `IAspect` references | `IOperationAspect` |

## Consequences

- `IQueryAspect : IOperationAspect` (ADR-0007) reads naturally: a query aspect is a
  named policy, just as an operation aspect is.
- All existing callers that pass `Aspect.NoOp` or an `IShapeAspect` to
  `EntityTransaction` methods continue to compile because `IShapeAspect : IOperationAspect`.
- File `IAspect.cs` is renamed to `IOperationAspect.cs` for filesystem consistency,
  though the rename is not strictly required at the compiler level.

> *`IOperationAspect` (base token) renamed back to `IAspect` due to Aspects ADR-0009. `IWriteAspect` was simultaneously renamed to `IOperationAspect`. The disambiguation rationale here remains valid; only the chosen name changed.*
