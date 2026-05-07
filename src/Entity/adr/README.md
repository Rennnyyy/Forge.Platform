# Architecture Decision Records — Forge.Entity

Slice-local decisions for the Entity library. Read after the [root ADRs](../../../adr/).

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — Identity is owned by Entity (no separate Identity project)](0001-identity-owned-by-entity.md)
- [0002 — `[Entity]` partial class + Roslyn source generator](0002-entity-partial-class-and-codegen.md)
- [0003 — IRI sealed once materialized](0003-iri-sealing.md)
- [0004 — Lazy references via awaitable `EntityRef<T>` + ambient `EntitySession`](0004-lazy-refs-and-session.md)
- [0005 — Predicate IRI is required on both `[Owning]` and `[Inverse]`](0005-predicate-iri.md)
- [0006 — Three identity strategies: Path / UuidV4 / UuidV5](0006-identity-strategies.md)
- [0007 — `[Required]` is metadata-only for now](0007-required-is-metadata-only.md)
- [0008 — Many-to-many relations via `EntityRefCollection<T>` on both sides](0008-many-to-many-inverse-collection.md)
- [0009 — Deferred (lazy) collections via `[Owning/Inverse(Lazy = true)]`](0009-deferred-collections.md)
- [0010 — `IEntityOptions` interface, ambient override, and `EntityOptionsInstance`](0010-entity-options-interface-and-di.md)
- [0011 — `Iri` static factory for IRI construction from plain text](0011-iri-factory.md)
- [0012 — `[Predicate]` attribute on scalar data properties](0012-predicate-attribute-for-data-properties.md)
- [0013 — `IEntityRepository<T>`, `IRdfMapper<T>`, and reflection-based v1](0013-entity-repository-and-rdf-mapper.md)
- [0014 — Podman as the preferred container runtime for integration tests](0014-podman-container-runtime.md)
- [0015 — ACID multi-operation transactions via `ITransactionalEntityStore`](0015-entity-transaction.md)
- [0016 — `IsIdentitySealed` is excluded from JSON serialization](0016-jsonignore-isidentitysealed.md)
