# 0006 — Three identity strategies: Path / UuidV4 / UuidV5

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Different entity kinds need different identity semantics. Some are naturally addressable by domain key (slug, ISBN), some are opaque, some are deterministic from a content hash.

## Options

1. **Three named strategies on `[Identity(IdentityGenerator.X)]`: `Path`, `UuidV4`, `UuidV5`.** Exactly one strategy per entity, enforced by analyzer.
2. Free-form generator delegate per class. Pro: maximum flexibility. Con: hidden behavior, no static analysis, harder to optimize/cache.
3. Single strategy (UUID only). Pro: minimal. Con: domain keys lost in opaque IRIs.

## Decision

Option 1.

- **`Path`** — concatenate `[IdentityPart(order)]`-marked properties (and entity references' own IRIs) under `{BaseIri}/{[Entity(Path)]?}/...`. Lists are joined by `IdentityPart.Separator` (default `/`). IRI is materialized when all parts are non-default; before then `Iri` access throws.
- **`UuidV4`** — random `Guid.NewGuid()` stored in a private backing field set in the parameterless constructor, persisted across hydration via an internal `Foo(Guid persistedUuid)` ctor that calls `HydrateIri`. Final IRI: `{BaseIri}/{Path?}/{guid:D}`.
- **`PropertyBasedEncoded`** — RFC 4122 v5 (SHA-1) deterministic GUID from `[IdentityPart(order)]`-marked properties.
  - **Namespace GUID** (the UUIDv5 hash seed): optional on `[Identity]`.
    - If provided (e.g. `Namespace = "6ba7b810-..."`), it is used verbatim.
    - If omitted, the generator emits code that derives it at runtime as
      `UuidV5(RFC4122-URL-namespace, EntityOptions.BaseIri + "/" + [Entity(Path)])`,
      scoping the identity space to the deployment base IRI automatically.
  - **Name string** (the hashed input): the identity-part values joined with `/`.
  - Final IRI shape identical to `Random`.

`[IdentityPart]` is meaningful for `Path` and `UuidV5`; ignored for `UuidV4`.

## Consequences

- New strategies are additive enum values + a generator branch + an ADR.
- Migrating an entity from one strategy to another is **not** supported; the IRI changes, so it counts as a fresh entity.
- The analyzer enforces exactly one `[Identity]` per `[Entity]` (`FORGE0002`).
