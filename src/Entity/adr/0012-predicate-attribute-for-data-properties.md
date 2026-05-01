# 0012 — `[Predicate]` attribute on scalar data properties

- **Status**: accepted
- **Date**: 2026-04-30
- **Author**: agent

## Context

`[Owning]` and `[Inverse]` declare predicate IRIs for entity references. Plain scalar
properties (e.g. `Bar.Name : string`) carry no such annotation; the persistence layer
introduced in root ADR-0005 has no way to map them to RDF predicates. Convention-based
mapping (camelCase property name under a base predicate IRI) is silently coupled to C#
identifiers — renaming a C# property would change graph semantics, the same hazard ADR-0005
addresses for relations.

## Options

1. **Introduce `[Predicate("name")]` on scalar data properties.** Predicate resolution
   identical to `[Owning]` / `[Inverse]`: short name → `{PredicateBaseIri}/{PredicatePath}/{name}`;
   absolute IRI (contains `:`) used verbatim. Properties without the attribute are not
   persisted by the default mapper.
2. Convention: every public settable scalar property is mapped to
   `{base}/{TypeName}/{propertyName}`. Pro: zero ceremony. Con: property renames silently
   change graph semantics; opt-in is impossible.
3. Reuse `[Owning(Predicate)]` for data properties.
   Pro: one attribute. Con: muddies semantics (owning is about relations, not literals);
   blocks future features like `[Owning]`-only validation.

## Decision

Option 1. New `Forge.Entity.PredicateAttribute` accepting one positional `predicate`
string. Same resolution rules as relation attributes (ADR-0005). Properties without the
attribute are ignored by the default `IRdfMapper<T>`.

`[IdentityPart]` properties are *implicitly* scalar data properties as far as RDF emission
is concerned — when also marked with `[Predicate]`, both attributes coexist (identity
participation + RDF predicate). When marked only with `[IdentityPart]`, the value still
participates in IRI generation but is not emitted as a separate triple.

## Consequences

- Persistence is opt-in at the property level — no surprise data leakage to RDF.
- Property renames do not change graph semantics.
- The default mapper (`Forge.Entity.Repository.ReflectionRdfMapper<T>`) reads this
  attribute via reflection; a future generator-emitted mapper (ADR-0013 phase 2) will
  consume the same attribute via syntax-tree introspection.
- `[Predicate]` is **metadata only at the entity level**: it has no runtime effect inside
  `Forge.Entity` itself, exactly like `[Required]` (ADR-0007). Its consumer is the
  Repository slice.
