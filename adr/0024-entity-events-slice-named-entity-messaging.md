# 0024 — Entity change event slice named `Forge.Entity.Messaging`, not `Forge.EntityEvents`

- **Status**: accepted; adjusts [0020](0020-messaging-abstractions-slice.md), [0021](0021-entity-change-event-stream.md)
- **Date**: 2026-05-14
- **Author**: agent

## Context

ADR-0021 and ADR-0020 referred to the entity change event stream slice as `Forge.EntityEvents`.
When the slice was implemented the name chosen was `Forge.Entity.Messaging` (`src/Entity.Messaging/`,
`Forge.Entity.Messaging.csproj`, root namespace `Forge.Entity.Messaging`).

ADR-0008 removed the `Entity.` prefix from *satellite* packages. However `Forge.Entity.Messaging`
is not a generic satellite; it is tightly scoped to entity change events produced by the Entity
type system. Its name mirrors the established sibling pattern:

| Slice | Namespace |
|-------|-----------|
| `Forge.Entity` | Core entity type system |
| `Forge.Entity.Generators` | Roslyn generator for entity types |
| `Forge.Entity.Messaging` | Entity change event stream (this slice) |

The `Entity.` infix here signals **membership in the Entity sub-family**, not a
generic-satellite relationship. This is the same rationale that kept `Forge.Entity.Generators`
prefix-unchanged in ADR-0008.

## Decision

The slice retains its implemented name: `Forge.Entity.Messaging`.

ADR-0021 and ADR-0020 are adjusted (not superseded) per ADR-0009: the identifier
`Forge.EntityEvents` used in their prose is corrected to `Forge.Entity.Messaging`.
No decision rationale or consequence in those ADRs changes.

## Changes table (ADR-0009 requirement)

| File adjusted | Old identifier | New identifier |
|---------------|---------------|----------------|
| `adr/0020-messaging-abstractions-slice.md` | `Forge.EntityEvents` | `Forge.Entity.Messaging` |
| `adr/0021-entity-change-event-stream.md` | `Forge.EntityEvents` | `Forge.Entity.Messaging` |

## Consequences

- `Forge.Entity.Messaging` is the canonical name; contributors must not use `Forge.EntityEvents`
  anywhere in code, comments, or documentation.
- Future entity-family slices follow the same pattern:
  `Forge.Entity.<Concern>` (e.g. `Forge.Entity.Validation` when that slice lands).
- ADR-0008's rule (drop the `Entity.` prefix from independent satellites) is unaffected;
  it applies to slices like `Forge.Repository`,`Forge.Aspects`, and `Forge.Operations` that
  stand on their own outside the Entity family.
