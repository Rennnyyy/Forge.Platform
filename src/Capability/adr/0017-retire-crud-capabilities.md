# 0017 — Retire `[CrudCapabilities]`, the CRUD generator, and related attributes

- **Status**: accepted; supersedes [0012](0012-crud-capability-generator.md), [0013](0013-crud-handler-marker-attribute.md), [0014](0014-crud-create-returns-already-exists-error.md), [0015](0015-crud-handlers-use-transactions-for-operation-aspects.md)
- **Date**: 2026-05-07
- **Author**: agent (user directive)

## Context

ADR-0012 introduced `Forge.Capability.Generators` as a Roslyn source generator that
emits five `ICapabilityHandler` implementations (Create, Read, Update, Delete, List)
for entities carrying `[CrudCapabilities]`. ADR-0013 added `[CrudCapabilityHandler]` as
a marker on each emitted class, used by `Forge.Capability.Http` to route those handlers
under `api/entities/` instead of `api/capabilities/`.

`Forge.Operations.Http` (Operations.Http ADR-0001) provides a lighter, direct REST path
for entity CRUD: annotate an entity with `[OperationEndpoints]`, call `MapOperations()`,
and five REST endpoints appear under `api/entities/{path}`. This path:

- Routes through `EntityTransaction` with optional `IOperationAspect` enforcement.
- Does not require a source generator.
- Does not run the capability dispatcher pipeline (no message-SHACL, no `CapabilityAspect`
  indirection, no `CapabilityContext` construction).
- Uses the shared `IExecutionAspectIriProvider` with `X-Forge-Operation-AspectIri` header.

With `[OperationEndpoints]` available, `[CrudCapabilities]` provides no value that
`[OperationEndpoints]` does not already cover, at the cost of maintaining a source
generator, a marker attribute, a dual-prefix routing branch in `Capability.Http`, and
five generated handler classes per entity.

## Decision

`[CrudCapabilities]`, `[CrudCapabilityHandler]`, `CrudMethod`, and the
`Forge.Capability.Generators` project are retired.

### Removed items

| Item | Previously defined in |
|------|-----------------------|
| `CrudCapabilitiesAttribute` | `Forge.Capability` |
| `CrudCapabilityHandlerAttribute` | `Forge.Capability` |
| `CrudMethod` enum | `Forge.Capability` |
| `Forge.Capability.Generators` project | `src/Capability.Generators/` |
| `Forge.Capability.Generators.Tests` project | `tests/Capability.Generators.Tests/` |

### Migration path

Replace `[CrudCapabilities]` on an entity class with `[OperationEndpoints]`. Call
`AddOperationEndpointsHttp(assembly)` instead of `AddCapabilityHandlersFromAssemblyContaining`.
Call `app.MapOperations()` in the pipeline. The aspect IRI moves from
`X-Forge-Capability-AspectIri` (with a `CapabilityAspect` indirection) to
`X-Forge-Operation-AspectIri` (operation aspect IRI supplied directly).

### `Capability.Http` routing impact

`[CrudCapabilityHandler]` was the column selector in the dual-prefix branch inside
`MapCapabilities()`. With the attribute deleted, the branch is removed. All capability
handlers register under `api/capabilities/` (see Capability.Http ADR-0011). This is an
extension of the route cleanup already started by ADR-0009 and ADR-0010.

## Consequences

- Entity CRUD over HTTP uses one mechanism: `[OperationEndpoints]` + `MapOperations()`.
- The capability dispatcher pipeline is reserved for non-trivial capabilities with
  message-level validation, `CapabilityContext`, and pluggable handlers.
- `Forge.Capability` and `Forge.Capability.Http` lose the `[CrudCapabilities]`-related
  surface area; the remaining API is smaller and sharper.
- Consumers that used generated CRUD capability handlers must migrate to
  `[OperationEndpoints]`. The URL shape changes:
  - Old: `POST api/entities/{entity}/{verb}` (verb in path, all POST)
  - New: `POST/GET/PUT/DELETE api/entities/{entity}` (REST verbs, IRI in query string)
  - Aspect header changes from `X-Forge-Capability-AspectIri` to
    `X-Forge-Operation-AspectIri`; the IRI value changes from a `CapabilityAspect` IRI
    to the `IOperationAspect` IRI directly.
