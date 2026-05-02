# 0002 — Second Roslyn generator for entity operations rather than extending Entity.Generators

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`Forge.Entity.Generators` already emits the structural partial half for each `[Entity]`
class (identity, refs, collections — ADR-0002). The Operations CRUD methods need to be
emitted into a *separate* partial file so they can be cleanly attributed to this slice.

The choice is between extending `Forge.Entity.Generators` with Operations-aware emission
versus introducing a second, independent generator in a new project.

## Options

1. **Extend `Forge.Entity.Generators` to also emit Operations methods.**
   Pro: single generator pass, single hint-file per entity.
   Con: `Forge.Entity.Generators` targets `netstandard2.0` and would have to encode
   awareness of `Forge.Operations` (a runtime library that lives in a different slice).
   This couples two slices at the generator level and blurs the slice boundary. It also
   violates ADR-0002 ("the generator project emits the second partial half" — singular; its
   scope is identity/refs/collections). ADR-0013 already deferred mapper generation to v2 to
   keep the emitter stable; adding a third concern makes the stability guarantee harder.

2. **New generator project `src/Operations.Generators/`**, targeting `netstandard2.0`,
   that hooks the same `Forge.Entity.EntityAttribute` attribute but emits a separate partial
   file with hint suffix `.g.ops.cs`. The generator has zero knowledge of the Entity slice's
   internal model; it only needs the class name, namespace, and `IsSealed` from the symbol.

## Decision

Option 2.

- Project: `src/Operations.Generators/Forge.Operations.Generators.csproj`
  (`netstandard2.0`, `IsRoslynComponent = true`).
- Emitter produces one file per entity: `{Namespace}.{TypeName}.g.ops.cs`.
- The generator depends only on `Microsoft.CodeAnalysis.CSharp` + `Microsoft.CodeAnalysis.Analyzers`;
  it does **not** reference `Forge.Entity` or `Forge.Operations` at build time —
  the references are in the user's compilation, not the generator binary.
- Consumer projects must reference:
  - `Forge.Operations` (runtime library)
  - `Forge.Operations.Generators` as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`

## Consequences

- `Forge.Entity.Generators` remains stable; its scope is unchanged.
- Each entity gets two generated partial files: `{Type}.g.cs` (identity/refs) and
  `{Type}.g.ops.cs` (CRUD operations).
- The Operations generator can evolve independently: adding a new method requires only
  touching `Operations.Generators`, not `Forge.Entity.Generators`.
- Generator tests follow the same assertion-based pattern used by `Entity.Generators.Tests`.
