# 0016 — Migrate `CapabilityResult<T>` and `CapabilityError` to `Forge.Execution`

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

Execution ADR-0001 established `Forge.Execution` as the shared contract layer for all
platform transport slices and defined `ExecutionResult<TResponse>` and `ExecutionError`
as the canonical execution outcome types. `Forge.Capability` currently owns two
structurally identical types — `CapabilityResult<TResponse>` and `CapabilityError` —
that live in the `Forge.Capability` namespace and are publicly visible on
`ICapabilityDispatcher<TCommand, TResponse>.DispatchAsync`.

Keeping both sets of types in parallel would:

1. Force `Operations.Http` to depend on `Forge.Capability` merely to reuse an error record.
2. Require gateway / middleware code to handle two structurally identical error shapes.
3. Contradict the single-vocabulary principle established in Execution ADR-0001.

## Options

1. **Remove `CapabilityError` and `CapabilityResult<TResponse>` from `Forge.Capability`;
   replace all usages with `ExecutionError` and `ExecutionResult<TResponse>` from
   `Forge.Execution`.** `ICapabilityDispatcher<TCommand,TResponse>.DispatchAsync`
   returns `ExecutionResult<TResponse>`. `Forge.Capability` gains a
   `ProjectReference` to `Forge.Execution`.

2. **Type-alias approach**: keep the old names as `using` aliases pointing to the new
   types. Pro: source-compatible for callers relying on the old name. Con: aliases are
   file-scoped in C#; a public alias cannot be exported in a public API; no real benefit
   over a clean rename.

3. **Keep `CapabilityResult<T>` / `CapabilityError` in `Forge.Capability`; make
   `Forge.Execution` produce the same types via a shared project reference.**
   Con: the dependency polarity is wrong — `Forge.Capability` would have to own types
   that should belong in the shared layer.

## Decision

Option 1. The old types are removed; all call sites are updated to the new names.

### Changes

| Action | File |
|--------|------|
| Add `<ProjectReference>` | `src/Capability/Forge.Capability.csproj` → `Forge.Execution` |
| Delete | `src/Capability/CapabilityError.cs` |
| Delete | `src/Capability/CapabilityResult.cs` |
| Modify | `src/Capability/ICapabilityDispatcher.cs` — return type `ExecutionResult<TResponse>` |
| Modify | `src/Capability/CapabilityDispatcher.cs` — return type + construction of `Ok`/`Fail` |
| Modify | `src/Capability.Http/EndpointRouteBuilderExtensions.cs` — pattern-match on `ExecutionResult<T>` |
| Modify | `tests/Capability.Tests/**` — all references |
| Modify | `tests/Capability.Http.Tests/**` — all references |
| Modify | `samples/Application.Sample/**` — all references |
| Adjust | Capability ADR-0005 — *`CapabilityResult<TResponse>` migrated to `ExecutionResult<TResponse>` in `Forge.Execution` due to ADR-0016.* |

### Error-code vocabulary preservation

The error code strings emitted by generated handlers and the dispatcher are unchanged:

| Code | Source |
|------|--------|
| `ALREADY_EXISTS` | Generated Create handler (Capability ADR-0014) |
| `NOT_FOUND` | Generated Read / Update / Delete handlers |
| `SHACL_VIOLATION` | `MessageAspectViolationException` (Capability.Http ADR-0007) |
| `ENTITY_SHACL_VIOLATION` | `AspectViolationException` (Capability.Http ADR-0008) |

These codes, now carried in `ExecutionError.Code`, remain stable across the migration.

### `CapabilityAspects` record

`CapabilityAspects` (added in an earlier sprint, holds per-slot aspect references inside
`CapabilityContext`) is not an execution-result type and is **not** migrated to
`Forge.Execution`. It remains in `Forge.Capability`.

## Consequences

- `Forge.Capability` gains one new `ProjectReference`; the rest of its dependency graph
  is unchanged.
- All public API callers of `ICapabilityDispatcher` receive `ExecutionResult<TResponse>`;
  the discriminated-union shape is identical — `Ok(Response)` / `Fail(ExecutionError)` —
  so the migration is mechanical (rename only).
- `Operations.Http` and any future transport slice use `ExecutionResult<T>` without a
  `Forge.Capability` dependency.
- The platform's error schema unifies: a single JSON shape, a single set of documented
  error codes, regardless of the transport slice that produced the response.
