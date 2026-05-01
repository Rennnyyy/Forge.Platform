# 0013 — `IEntityRepository<T>`, `IRdfMapper<T>`, and reflection-based v1

- **Status**: accepted
- **Date**: 2026-04-30
- **Author**: agent

## Context

Root ADR-0005 introduces a Repository slice. This ADR pins down the shape of the
abstractions inside it and the materialization strategy.

The mapping surface (entity ↔ triples) needs to honor:

- `EntityBase.HydrateIri(...)` — already exists, seals identity post-load.
- IRI sealing (ADR-0003) — identity-part setters call `GuardIdentityMutation()`, which
  throws if `_iri` is already set. Hydration must populate parts **before** IRI is sealed
  or via private-field writes that bypass the public setter.
- `EntityRef<T>` lazy resolution (ADR-0004) — the materializer should set refs to
  `EntityRef<T>.ForIri(iri)` and let `EntitySession` handle resolution on access.
- Eager owning collections vs. deferred (ADR-0009) — the materializer pre-populates IRIs
  for eager collections; deferred collections are left untouched (they self-load on first
  access).

## Options

1. **Define `IRdfMapper<T>` as the per-type mapping contract; ship a single
   reflection-based `ReflectionRdfMapper<T>` in v1; defer source-generator emission to v2.**
   Identity-part hydration uses `FieldInfo.SetValue` on the private `__forge_part_*` backing
   fields *before* `HydrateIri` is called. This keeps the existing emitter untouched and
   delivers a working slice now.
2. Extend `Forge.Entity.Generators` immediately to emit per-type mappers. Pro: AOT-friendly,
   no reflection. Con: doubles the surface area of the change; risks regressions in the
   stable emitter; harder to iterate on the mapping shape.
3. Hand-author one mapper per entity in user code. Pro: explicit. Con: reintroduces the
   boilerplate that ADR-0002 deliberately removed.

## Decision

Option 1.

### `IEntityStore`

Backend boundary. Implementations: `InMemoryEntityStore` (dotNetRDF), `GraphDbEntityStore`
(HTTP). Implements `IEntityLoader` and `ICollectionLoader` so `EntitySession` integrates
without an adapter. Single store services all registered entity types.

```csharp
public interface IEntityStore : IEntityLoader, ICollectionLoader, IAsyncDisposable
{
    ValueTask<T?> LoadAsync<T>(string iri, CancellationToken ct = default) where T : class, IEntity;
    ValueTask SaveAsync<T>(T entity, WriteMode mode, CancellationToken ct = default) where T : class, IEntity;
    ValueTask DeleteAsync(string iri, CancellationToken ct = default);
    IAsyncEnumerable<T> QueryByTypeAsync<T>(CancellationToken ct = default) where T : class, IEntity;
}
```

### `IEntityRepository<T>`

Typed facade over `IEntityStore` for application code. One per entity type registered in DI.

### `IRdfMapper<T>`

Per-type mapping contract; consumed by the materializer.

```csharp
public interface IRdfMapper<T> : IRdfMapper where T : class, IEntity
{
    T Hydrate(string iri, RdfGraph subjectGraph, IEntityMaterializerContext ctx);
    void Project(T entity, IRdfTripleSink sink);
}
```

Non-generic `IRdfMapper` carries metadata (entity type, type IRI, predicate map, identity
strategy) so decorators (validation, change tracking) can introspect without resolving the
generic.

### `ReflectionRdfMapper<T>` — v1

- Reads `[Entity]`, `[Identity]`, `[IdentityPart]`, `[Predicate]`, `[Owning]`, `[Inverse]`.
- Hydration order:
  1. For Path / UuidV5: write each `[IdentityPart]` value into the private
     `__forge_part_{name}` field via reflection (bypasses init-only and `GuardIdentityMutation`).
  2. For UuidV4 / UuidV5: write `__forge_identityUuid` field.
  3. Call `HydrateIri(iri)` to seal.
  4. Set `[Predicate]` scalar properties (skip `[IdentityPart]`-only properties — already set).
  5. Set owning refs to `EntityRef<T>.ForIri(targetIri)`.
  6. Pre-populate eager owning collection IRIs via `EntityRefCollectionImpl<T>.AddAsync(stub)`.
     Deferred collections are not touched.
- Projection (write):
  1. Emit `<iri> rdf:type <typeIri>`.
  2. Emit `[Predicate]` scalar properties as triples.
  3. Emit `[IdentityPart]` properties as triples *if* they also carry `[Predicate]` (otherwise
     identity is encoded in the IRI alone).
  4. Emit `[Owning]` single refs as `<iri> <predicate> <targetIri>`.
  5. Emit `[Owning]` collections as ordered `rdf:List` chains.
  6. Skip all `[Inverse]` references — owning side is the source of truth.

### Migration path to a generated mapper (phase 2)

The `IRdfMapper<T>` API is stable. `ReflectionRdfMapper<T>` is one implementation. A future
ADR will introduce `Forge.Entity.Generators` emission of `Generated{TypeName}RdfMapper`
classes that implement the same interface, registered ahead of the reflection mapper in
the registry. No application change required.

## Consequences

- Slice ships with full read/write coverage now, paid in microbenchmarks (reflection per
  property). Acceptable for the typical RDF workload (network-bound).
- `__forge_part_*` field naming is now an *internal contract* between
  `Forge.Entity.Generators` and `Forge.Entity.Repository`. A test in
  `Entity.Repository.Tests` asserts the contract holds for known entity samples.
- The generator team can change identity-part field naming only by updating the contract
  in lockstep with `ReflectionRdfMapper<T>`.
- AOT consumers must wait for phase 2 (generated mappers) before trimming.
