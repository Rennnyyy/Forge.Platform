# 0011 — `Iri` static factory for IRI construction from plain text

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: agent

## Context

In several scenarios — integration tests, link-header generation, manual references in migrations —
a caller has a string like `"/entity/myentity"` or knows the entity type (`Bar`) plus an identity
segment and wants the fully-qualified IRI without constructing an entity instance. There is currently
no helper for this; callers must manually concatenate `EntityOptions.Current.BaseIri + "/"` which
is error-prone (double slashes, missing path prefix).

## Options

1. **`Iri` static class with two factory methods: `FromBaseUrl(string)` and `FromEntity<T>(string)`.**
   Both read from `EntityOptions.Current`, so they participate in the ambient override from ADR-0010.

2. Extension methods on `string`. Pro: fluent. Con: pollutes `string`, confusing discoverability.

3. Instance methods on `EntityOptions` / `IEntityOptions`. Pro: no additional type. Con: couples
   the options interface to IRI construction; splits the factory surface across files.

## Decision

Option 1.

### `Iri.FromBaseUrl(string path)`

Combines `EntityOptions.Current.BaseIri` with the given relative path. Leading slashes in
`path` are normalized (trimmed) to prevent double slashes.

```csharp
Iri.FromBaseUrl("/entity/myentity")
// → "https://forge.example/entity/myentity"
```

### `Iri.FromEntity<T>(string identity)`

Reads the `[Entity(Path = "…")]` attribute from `T` via reflection, then builds:
`{BaseIri}/{entityPath}/{identity}`. If `Path` is not set, falls back to the lower-cased
type name. Throws `InvalidOperationException` if `T` is not decorated with `[Entity]`.

```csharp
Iri.FromEntity<Bar>("myentity")
// → "https://forge.example/bars/myentity"
```

Reflection is acceptable here: `FromEntity<T>` is not on the identity-materialization hot path;
it is a convenience helper for constructing reference IRIs in test setup and manual plumbing code.

### Class name

`Iri` (title-case) follows the C# convention for type names and is short. The existing codebase
uses the string `iri` as a local variable name in several places, which is unambiguous due to
different casing.

## Consequences

- IRI construction from plain text is consistent (always routes through `EntityOptions.Current`).
- The helper is usable inside `EntityOptions.Use(…)` scopes, so test overrides work automatically.
- Reflection on `[Entity]` attribute is done on every call; if hot-path usage ever arises, caching
  can be added inside `FromEntity<T>` without changing its signature.
