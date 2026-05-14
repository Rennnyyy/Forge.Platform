# 0005 — EntityGuardViolationException: base for guard-layer domain violations

- **Status**: accepted
- **Date**: 2026-05-11
- **Author**: agent

## Context

`Forge.Branch` contains two guard-layer exceptions that represent client-caused
data integrity errors:

- `SnapshotImmutabilityViolationException` — thrown when a write targets a frozen
  snapshot named graph (Branch ADR-0002).
- `BranchProtectionViolationException` — thrown when an operation would delete the
  default branch or drop the management graph (Branch ADR-0001).

Both conditions map to HTTP 422 Unprocessable Entity at the endpoint layer.
`ExecutionEndpointHelper` (in `Forge.Execution.Http`) is the single translation point
for domain exceptions to HTTP status codes, and it already depends on `Forge.Repository`
(for `EntityAlreadyExistsException`). Until this ADR, both guard exceptions extended
`InvalidOperationException`: there was no common base class, so they escaped every
`catch` block in `ExecutionEndpointHelper` and surfaced as unhandled 500 responses.

Adding individual `catch (SnapshotImmutabilityViolationException)` and
`catch (BranchProtectionViolationException)` clauses to `ExecutionEndpointHelper` would
require adding a `ProjectReference` to `Forge.Branch` from `Forge.Execution.Http`,
creating an import inversion (Http pulling in a domain slice).

## Options

**Option A** — Catch `InvalidOperationException` broadly in `ExecutionEndpointHelper`.
Con: too wide; any unexpected `InvalidOperationException` from third-party code would
be silently swallowed as a 422 instead of crashing visibly as a 500.

**Option B** — Add `catch (SnapshotImmutabilityViolationException)` etc.; add
`Forge.Branch` `ProjectReference` to `Forge.Execution.Http`.
Con: explicit dependency inversion; Http layer must know about Branch business logic.

**Option C** — Define `EntityGuardViolationException : InvalidOperationException` in
`Forge.Repository` (already on `Forge.Execution.Http`'s dependency graph). Guard
exceptions in `Forge.Branch` extend it. `ExecutionEndpointHelper` catches the base type.
Pro: no new project references needed; extensible to future guard violations in any slice
that depends on `Forge.Repository`; semantically precise.

## Decision

**Option C.**

`EntityGuardViolationException : InvalidOperationException` is added to `Forge.Repository`
as an `abstract` base class. `SnapshotImmutabilityViolationException` and
`BranchProtectionViolationException` in `Forge.Branch` now extend it instead of
`InvalidOperationException` directly.

`ExecutionEndpointHelper.InvokeAsync` adds a `catch (EntityGuardViolationException)`
clause that returns 422 with error code `ENTITY_GUARD_VIOLATION`.

## Consequences

- All future guard-layer violations that want automatic 422 mapping simply extend
  `EntityGuardViolationException` — no changes to `ExecutionEndpointHelper` required.
- The base class is `abstract` to prevent accidental direct instantiation.
- `Forge.Execution.Http` gains no new `ProjectReference`.
- Existing callers of `SnapshotImmutabilityViolationException` and
  `BranchProtectionViolationException` are unaffected: the concrete types are unchanged.
