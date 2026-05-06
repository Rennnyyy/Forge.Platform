# 0011 — Remove `api/entities/` prefix from `MapCapabilities()`

- **Status**: accepted; supersedes [0010](0010-restore-entities-prefix-for-crud-handlers.md)
- **Date**: 2026-05-07
- **Author**: agent (user directive)

## Context

ADR-0006 introduced a dual-prefix branch in `MapCapabilities()`:
- Handlers with `[CrudCapabilityHandler]` → `api/entities/`
- All other handlers → `api/capabilities/`

ADR-0009 removed that branch (all under `api/capabilities/`). ADR-0010 restored it.

With Capability ADR-0017 retiring `[CrudCapabilities]` and the
`Forge.Capability.Generators` project entirely, `[CrudCapabilityHandler]` is deleted
from `Forge.Capability`. There are no longer any handlers carrying the attribute, making
the dual-prefix branch dead code.

## Decision

Remove the `isCrud` branching from `MapCapabilities()`. All capability handlers register
under `api/capabilities/` unconditionally. The `CrudCapabilityHandlerAttribute` type
reference is removed from `EndpointRouteBuilderExtensions`.

Entity CRUD over HTTP is exclusively the domain of `MapOperations()` under
`api/entities/`, as originally intended by Operations.Http ADR-0001.

## Consequences

- `MapCapabilities()` is simpler: one prefix, no attribute inspection.
- `api/entities/` is the exclusive domain of `MapOperations()`.
- `api/capabilities/` is the exclusive domain of `MapCapabilities()`.
- No migration cost: there are no remaining `[CrudCapabilityHandler]` handlers.
