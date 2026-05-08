# 0001 — `Forge.Aspects.Abstractions`: break the circular dependency between `Forge.Aspects` and `Forge.Repository`

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent (pending user acceptance)

## Context

`Forge.Repository` defines the `IAspect` token and `Aspect` well-known singletons
so that callers can reference a named validation policy without depending on the
full aspects engine. `Forge.Aspects` depends on `Forge.Repository` to access
`IEntityStore` for Context SPARQL passes and `ITransactionalEntityStore` for the
write-path aspect wrappers.

As new aspect-contract types were added (`IOperationAspect`, `IQueryAspect`,
`IMessageAspect`, `MessageKind`, `CapabilityAspect`, `IAspectStore`) they were
initially placed in `Forge.Aspects`, which already depended on `Forge.Repository`.
Any caller that only needed the contracts — in particular `Forge.Capability`, which
only dispatches against the contracts — would need to take the full `Forge.Aspects`
dependency, pulling in dotNetRDF and the complete engine.

Additionally, once `Forge.Aspects` started decorating `ITransactionalEntityStore`
with `AspectEnforcingTransactionalStore`, it became impossible to move the contracts
back to `Forge.Repository` without creating a mutual dependency:
`Forge.Repository ← Forge.Aspects ← Forge.Repository`.

## Decision

Introduce a new assembly `Forge.Aspects.Abstractions` that:

1. Holds only **pure contracts and well-known constants** — no engines, no
   infrastructure, no external dependencies beyond the BCL.
2. Depends on **nothing** from the Forge stack (no `Forge.Repository`,
   no `Forge.Aspects`).
3. Is depended on by **both** `Forge.Repository` and `Forge.Aspects`, breaking the
   potential cycle.

Types moved to `Forge.Aspects.Abstractions`:

| Type | Source |
|------|--------|
| `IAspect` | `Forge.Repository` |
| `Aspect` | `Forge.Repository` |
| `IAspectStore` | `Forge.Aspects` |
| `IOperationAspect` | `Forge.Aspects` |
| `IQueryAspect` | `Forge.Aspects` |
| `IMessageAspect` | `Forge.Aspects` |
| `MessageKind` | `Forge.Aspects` |
| `CapabilityAspect` | `Forge.Capability` |
| `AspectNotFoundException` | `Forge.Aspects` |

## Consequences

- `Forge.Capability` can depend on `Forge.Aspects.Abstractions` alone (for
  `CapabilityAspect`, `IOperationAspect`, `IMessageAspect`) without pulling in the
  full `Forge.Aspects` engine assembly.
- `Forge.Repository` no longer owns `IAspect` / `Aspect`; callers holding only
  `Forge.Repository` must add a reference to `Forge.Aspects.Abstractions` to use
  the aspect token types directly (or get them transitively via `Forge.Aspects`).
- Dependency graph becomes a strict DAG: `Forge.Aspects.Abstractions` ←
  `Forge.Repository` ← `Forge.Aspects` ← `Forge.Capability`.
- The assembly is tiny and has no external NuGet dependencies, making it safe to
  reference from any layer without weight concerns.
