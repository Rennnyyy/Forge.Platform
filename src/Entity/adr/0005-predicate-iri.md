# 0005 — Predicate IRI is required on both `[Owning]` and `[Inverse]`

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Entity references are not anonymous links — in RDF every relation has a stable predicate IRI. The mapping from a C# property to that predicate must be explicit, discoverable, and survive renames of the property.

## Options

1. **`Predicate` is required on both `[Owning]` and `[Inverse]`.** Short names are resolved against `EntityOptions.PredicateBaseIri` + `[Entity(PredicatePrefix = "...")]`. Absolute IRIs are accepted verbatim.
2. Required only on `[Owning]`; inverse reuses the owning side's value via reflection or codegen lookup. Pro: less typing. Con: rename-fragile; the inverse-side reader has to look up the owning side, which crosses class boundaries during emission.
3. Optional, defaulting to `{base}/{TypeName}/{PropertyName}`. Pro: zero ceremony. Con: predicates become coupled to C# names; renames silently change graph semantics.

## Decision

Option 1. The originally drafted ADR title ("inverse reuses it") was rejected during the design dialogue; both sides must declare the predicate.

- `[Owning(Predicate = "hasBar")]` — required.
- `[Inverse("PrimaryBar", Predicate = "owns")]` — required.
- Resolution: if the value is an absolute IRI (contains `:`), use as-is; otherwise prepend `{EntityOptions.PredicateBaseIri}/{[Entity(PredicatePrefix)]?}/`.
- `EntityOptions.PredicateBaseIri` defaults to `{EntityOptions.BaseIri}/predicates`.

The generator currently records the predicate as attribute metadata on the generated half. A runtime accessor (`Foo.Metadata.PrimaryBar.Predicate`) is **not** added yet — deferred until the persistence layer needs it.

## Consequences

- Property renames do not change graph semantics.
- An owning/inverse pair with mismatched predicates is a future analyzer warning candidate (logical inverses usually share the same canonical predicate, with directionality implicit). Not enforced today.
- When the persistence layer arrives, it can read the predicate via attribute reflection over the generated metadata.
