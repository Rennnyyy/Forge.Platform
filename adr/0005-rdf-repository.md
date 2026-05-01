# 0005 — RDF-backed Entity Repository as a separate slice

- **Status**: accepted
- **Date**: 2026-04-30
- **Author**: agent

## Context

`Forge.Entity` defines a runtime type system (entities, identity, refs, options) but has no
persistence. Production needs to load and persist entities against an RDF graph store
(Ontotext GraphDB on Docker), and tests need to do the same against an in-memory store
without infrastructure. A future need is anticipated for SPARQL query construction and
rewriting (security filters, named-graph routing, federation, property-path expansion),
but its scope is uncertain.

## Options

1. **Introduce three new slices** —
   `Forge.Entity.Repository` (abstractions + reflection-based mapper),
   `Forge.Entity.Repository.InMemory` (dotNetRDF in-memory backend),
   `Forge.Entity.Repository.GraphDb` (Ontotext GraphDB HTTP backend).
   Each backend ships its own DI extension. Backend selection is configuration-driven
   (`Forge:EntityRepository:Backend = InMemory | GraphDb`). Query construction is a
   future fourth slice (`Forge.Entity.Sparql`); v1 uses templated SPARQL strings inside
   the backends.
2. Single `Forge.Entity.Persistence` slice with both backends inside.
   Pro: fewer projects. Con: every consumer drags both backends and their dependencies
   (dotNetRDF + HttpClient stack); breaks the slice-per-concern discipline of the platform.
3. Persistence inside `Forge.Entity` core.
   Pro: zero-coupling for consumers. Con: forces dotNetRDF onto every consumer including
   pure-domain projects with no persistence concern; ADR-0001 explicitly keeps identity
   in core, persistence is a different concern.

## Decision

Option 1.

### Slice layout

| Slice | Purpose |
|-------|---------|
| `src/Entity.Repository/` | `IEntityStore`, `IEntityRepository<T>`, `IEntityMaterializer`, `IRdfMapper<T>`, `EntitySession`/loader integration, RDF model types (`RdfTerm`, `RdfTriple`, `RdfGraph`), reflection-based mapper |
| `src/Entity.Repository.InMemory/` | `InMemoryEntityStore` backed by a dotNetRDF `TripleStore`; fluent builder for Turtle fixtures |
| `src/Entity.Repository.GraphDb/` | `GraphDbEntityStore` against the Ontotext SPARQL endpoint over `HttpClient`; `DelegatingHandler`-based auth |
| `src/Entity.Sparql/` | **Deferred.** Query model + emitter + rewriter pipeline. Phase-2; `IEntityStore` is the seam. |

### Backend selection

`services.AddForgeEntityRepository(configuration)` reads the `Backend` discriminator from
`Forge:EntityRepository:Backend` and wires the corresponding store. Each backend's
DI extension (`AddInMemoryEntityStore`, `AddGraphDbEntityStore`) is also callable
directly for tests and code-first wiring.

### v1 mapper is reflection-based

`IRdfMapper<T>` is implemented in `Forge.Entity.Repository` by a single
`ReflectionRdfMapper<T>` that reads `[Entity]`, `[IdentityPart]`, `[Predicate]` (new),
`[Owning]`, `[Inverse]` via reflection. ADR-0013 explains why the source generator is not
extended in this phase and what the migration path looks like.

### Validation is *not* a repository concern

`IEntityRepository<T>.SaveAsync` does not run validation. Validation is its own slice
(future `Forge.Entity.Validation`). For pre-write hooks, layer a decorator
(`ValidatingEntityRepository<T>`) over `IEntityRepository<T>`. Repository surface stays
small and single-purpose.

### Write semantics

`SaveAsync` supports two modes (`WriteMode.Create` / `WriteMode.Replace`). Replace is a
full-PUT: all triples for the entity's IRI in the target named graph are deleted, then
the new state is asserted. Owning refs and owning collections are written; inverse refs
and inverse collections are skipped (the owning side carries the truth). Owning collections
are written as ordered `rdf:List` chains.

## Consequences

- Pure-domain projects do not pay for persistence.
- Consumers pick exactly the backends they need (NuGet-level isolation).
- Adding a future backend (e.g. Stardog, RDFox) is a new sibling slice with no churn in
  core or other backends.
- `Forge.Entity.Sparql` can land later without forcing API changes in `Forge.Entity.Repository`
  because `IEntityStore` accepts opaque SPARQL queries today.
- `Microsoft.Extensions.*` becomes a dependency of `Forge.Entity.Repository`. Acceptable —
  every realistic consumer is on the MS hosting stack.
