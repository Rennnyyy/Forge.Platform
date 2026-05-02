# 0002 — No native GraphDB SHACL

- **Status**: accepted
- **Date**: 2026-05-02
- **Author**: agent

## Context

Ontotext GraphDB supports server-side SHACL 1.0 enforcement: shapes can be loaded into
a dedicated `http://rdf4j.org/schema/rdf4j#SHACLShapeGraph` named graph, and GraphDB
will validate each write against them automatically. This is surfaced in client
configurations as flags such as `useNativeShacl`, `enableNativeShacl`, or similar
properties on the repository connection settings.

Enabling native GraphDB SHACL would mean:

- Shapes must be maintained as a side-loading concern in the graph store, separate from
  code-origin shape registration.
- Validation fires after the write reaches the server, not before — rollback semantics
  differ per backend.
- InMemory has no equivalent mechanism; cross-backend parity is lost.
- The per-operation, caller-declared aspect model (ADR-0003) cannot be mapped onto
  store-side enforcement because the store has no concept of "which aspect applies to
  this specific operation."
- Mixing engine-side and store-side validation creates split responsibility: violations
  from two different systems, different error models, different rollback points.

## Options

1. **Engine-only SHACL. Never delegate to GraphDB.** All SHACL evaluation is performed
   by `IAspectEngine` using dotNetRDF's `ShapesGraph.Validate`. `GraphDbOptions` must
   never expose a native-SHACL toggle. A test asserts this by reflection.
2. **Opt-in native GraphDB SHACL for the Context pass.** Local pass always engine-side;
   Context pass can optionally be delegated to native SHACL when on GraphDB. Con: dual
   path; InMemory tests do not cover the GraphDB path; shapes must be kept in sync
   between registry and graph store; per-operation aspect model breaks.
3. **Native SHACL as the primary mechanism; engine as InMemory fallback.** Con: two
   completely different validation pipelines; high complexity; InMemory tests become a
   poor proxy for production behaviour.

## Decision

Option 1. All SHACL evaluation is performed by the Aspects engine. GraphDB is used
as a dumb triple store for the purposes of this slice.

### Enforcement

`GraphDbOptions` **must not** contain any member named `UseNativeShacl`,
`EnableNativeShacl`, `NativeShaclEnabled`, or any name that matches the pattern
`*NativeShacl*` or `*Shacl*`. Test case 7 in `Entity.Aspects.Tests` asserts this via
reflection and must remain green. Any future PR that adds such a member will fail CI.

## Consequences

- `Forge.Entity.Aspects` has no reference to `Forge.Entity.Repository.GraphDb`. The
  test project may reference `GraphDb` only for the reflection guard test.
- GraphDB shapes are not loaded into the graph store. Shape management is entirely
  in-process via `IShapeRegistry`.
- If native GraphDB SHACL is ever required (e.g. for inter-process enforcement), a new
  ADR must supersede this one and the test guard must be updated explicitly.
