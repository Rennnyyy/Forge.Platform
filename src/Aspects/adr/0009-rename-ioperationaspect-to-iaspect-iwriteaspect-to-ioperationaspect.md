# 0009 — Restore `IAspect` as base token; rename `IWriteAspect` → `IOperationAspect`

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0006 renamed the thin identity token `IAspect` → `IOperationAspect` to disambiguate
it from the CUD write interface once `IQueryAspect` was introduced. That rename was
well-motivated but picked the wrong name: `IOperationAspect` is maximally generic (every
aspect token is, in some sense, "operational"), yet it was assigned to the *base* token
rather than to the interface that is literally attached to a `TransactionOperation`.

Meanwhile ADR-0008 renamed the CUD write interface `IShapeAspect` → `IWriteAspect`. That
left the following family:

| Interface | Describes |
|---|---|
| `IOperationAspect` (base) | Thin identity token — name only |
| `IWriteAspect : IOperationAspect` | CUD write validation |
| `IQueryAspect : IOperationAspect` | Read / query validation |
| `IMessageAspect : IOperationAspect` | Message validation |

The problem: `IOperationAspect` is attached to the *base*, not to the *write* interface.
`TransactionOperation.Aspect` has type `IOperationAspect` — meaning the property reads
*"the operation-aspect of this operation"*, a tautology. Callers who see
`entity, IOperationAspect aspect` in an `EntityTransaction` overload cannot tell from the
name alone whether they must pass a write-validation aspect or just any named token.

The correct name for the base — the one that signals "just a named policy, no shape
data" — is `IAspect`. The correct name for the CUD write interface — the one attached to
`TransactionOperation` — is `IOperationAspect`.

## Options

1. **Restore `IAspect` as the base token; promote `IWriteAspect` → `IOperationAspect`.**
   `TransactionOperation.Aspect` becomes `IAspect Aspect` — unambiguous.
   `IOperationAspect : IAspect` names the validation contract for transaction operations.
   `IQueryAspect : IAspect`, `IMessageAspect : IAspect` — all sub-interfaces now extend
   a cleanly named base.
2. **Keep current names.** No churn. Con: the tautological naming remains; new
   contributors routinely misread which interface to pass to `EntityTransaction`.
3. **Rename base to `IAspectToken`.**  Clarifies it is merely a token. Con: introduces
   a suffix convention (`Token`) that no other type in the codebase uses; awkward.

## Decision

Option 1.

### Changes

| Location | Before | After |
|---|---|---|
| `Forge.Repository/IOperationAspect.cs` | `public interface IOperationAspect` | `public interface IAspect` |
| File renamed | `IOperationAspect.cs` | `IAspect.cs` |
| `Forge.Repository/Aspect.cs` | `public static readonly IOperationAspect NoOp` | `public static readonly IAspect NoOp` |
| `Forge.Repository/Aspect.cs` | `internal sealed class NoOpAspect : IOperationAspect` | `internal sealed class NoOpAspect : IAspect` |
| `Forge.Repository/TransactionOperation.cs` | `IOperationAspect _noOp` / `IOperationAspect Aspect` | `IAspect _noOp` / `IAspect Aspect` |
| `Forge.Repository/EntityTransaction.cs` | all `IOperationAspect aspect` params | `IAspect aspect` |
| `Forge.Aspects/IWriteAspect.cs` | `public interface IWriteAspect : Forge.Repository.IOperationAspect` | `public interface IOperationAspect : Forge.Repository.IAspect` |
| File renamed | `IWriteAspect.cs` | `IOperationAspect.cs` |
| `Forge.Aspects/IQueryAspect.cs` | `: IOperationAspect` | `: IAspect` |
| `Forge.Aspects/IMessageAspect.cs` | `: Forge.Repository.IOperationAspect` | `: Forge.Repository.IAspect` |
| `Forge.Aspects/IAspectResolver.cs` | `IWriteAspect Resolve(IOperationAspect …)` | `IOperationAspect Resolve(IAspect …)` |
| `Forge.Aspects/IShapeRegistry.cs` | `IWriteAspect?/Register/TryGet` with `IOperationAspect` | `IOperationAspect?/Register/TryGet` with `IAspect` |
| `Forge.Aspects/ShapeRegistry.cs` | all `IWriteAspect` and `IOperationAspect` refs | `IOperationAspect` and `IAspect` |
| `Forge.Aspects/InlineTtlWriteAspect.cs` | `: IWriteAspect` | `: IOperationAspect` |
| `tests/Aspects.Tests/MessageAspectTypeTests.cs` | `Forge.Repository.IOperationAspect` | `Forge.Repository.IAspect` |

### ADRs adjusted (per root ADR-0009)

The following ADRs have their decisions and consequences unchanged; only identifier names
appearing in them are affected. Each receives an inline note per root ADR-0009.

| ADR | Note added |
|---|---|
| Aspects ADR-0004 | `IAspect` name restored |
| Aspects ADR-0006 | Base token reverted `IOperationAspect` → `IAspect` |
| Aspects ADR-0007 | `IQueryAspect` base `IOperationAspect` → `IAspect` |
| Aspects ADR-0008 | `IWriteAspect` renamed to `IOperationAspect` |
| Capability ADR-0001 | `IOperationAspect` base → `IAspect`; `IWriteAspect` → `IOperationAspect` |
| Validation ADR-0001 | `IOperationAspect` ref → `IAspect` |

## Consequences

- `TransactionOperation.Aspect` is typed `IAspect` — reads naturally as *"the aspect
  of this operation"*.
- `IOperationAspect : IAspect` is the CUD write interface, precisely named after its
  home (`TransactionOperation`).
- The three concrete sub-interfaces follow a uniform `I<Scope>Aspect` pattern:
  `IOperationAspect` (write), `IQueryAspect` (read), `IMessageAspect` (message).
- `Aspect.NoOp` returns to type `IAspect` — cleanly generic, no scope leakage.
- All callers that previously passed `Aspect.NoOp` or an `IWriteAspect` to
  `EntityTransaction` methods continue to compile because `IOperationAspect : IAspect`.
