# 0004 — Default namespace = Forge.\<library\>

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Each slice ships as a standalone library. Type discovery should be predictable for users referencing multiple slices.

## Options

1. **`RootNamespace = Forge.<Library>` per project**, matching the assembly name.
2. Shared `Forge` namespace across all libraries. Pro: short. Con: collisions; every slice's types pollute the same name.
3. Slice-internal nesting (`Forge.Platform.Entity`). Pro: unambiguous. Con: verbose; the "Platform" segment adds nothing.

## Decision

Option 1. Project name = assembly name = root namespace, all in the form `Forge.<Library>`. Sub-namespaces inside the library (e.g. `Forge.Entity.Attributes`) are allowed where they aid discovery.

## Consequences

- Importing a slice exposes a single top-level namespace per slice.
- Internal helper namespaces are free to evolve without touching the public surface.
