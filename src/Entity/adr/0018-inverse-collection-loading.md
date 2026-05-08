# 0018 — Inverse collection hydration via `IInverseRefLoader`

- **Status**: accepted
- **Date**: 2026-05-08
- **Author**: agent

## Context

ADR-0013 deferred hydration of `[Inverse]`-annotated collection properties:
`ReflectionRdfMapper<T>` skipped all `[Inverse]` collections at load time with the comment
"inverse collections handled by generator-emitted deferred impl". ADR-0009 introduced
`DeferredEntityRefCollectionImpl<T>` which calls `ICollectionLoader.LoadCollectionIrisAsync`
on first access. However, that method performs a **forward** query
(`<ownerIri> <predicate> ?list`). For inverse collections the data is stored on the *owning*
entity — the predicate's subject is the other entity's IRI — so a forward query always
returns empty.

Concretely: `Genre.ProducedBy (EntityRefCollection<Studio>)` should contain every `Studio`
whose `hasGenre` rdf:List includes the genre's IRI. `LoadCollectionIrisAsync(genreIri,
"hasGenre")` looks for triples whose *subject* is `genreIri`, which do not exist in the
graph.

ADR-0017 solved the analogous problem for inverse *single* refs by adding
`IInverseRefLoader.LoadInverseRefIriAsync`. The same mechanism is extended to collections
here.

## Options

1. **Extend `IInverseRefLoader` with `LoadInverseCollectionIrisAsync<T>` and hydrate
   inverse collections inside `ReflectionRdfMapper.HydrateAsync` (new step 6),
   symmetrically with single-ref step 5.**
   Pro: mirrors ADR-0017 exactly; no new collection types; works through the existing
   `IInverseRefLoader` already passed at hydration time; no changes to the generator or
   the delegating stores.
   Con: every `[Inverse]` collection on a loaded entity incurs one additional store
   round-trip per property.

2. Add a new `DeferredInverseEntityRefCollectionImpl<T>` wired by the generator for
   `[Inverse(Lazy = true)]` collections, casting `EntitySession.Current.Loader` to
   `IInverseRefLoader` on first access.
   Con: introduces a new public collection type; requires generator changes; requires all
   delegating stores (`GuardedTransactionalStore`, `AspectEnforcingEntityStore`,
   `AspectEnforcingTransactionalStore`) to be updated to expose `IInverseRefLoader`;
   adds `Lazy = true` annotation noise to the domain model.

3. Leave behaviour as-is and document it as a known limitation.
   Rejected immediately.

## Decision

Option 1.

### `IInverseRefLoader` extension

```csharp
IAsyncEnumerable<string> LoadInverseCollectionIrisAsync<T>(
    string targetIri,
    string predicate,
    CancellationToken cancellationToken = default)
    where T : class, IEntity;
```

### `ReflectionRdfMapper<T>` additions

`TypePlan` gains `IReadOnlyList<InverseCollectionProp> InverseCollections`.

For each `[Inverse]` property whose CLR type is `EntityRefCollection<T>` (kind
`InverseCollection`):
- Resolve the declared `Predicate` string to an absolute IRI using the **target entity's**
  `PredicatePath` — i.e. the type argument of the collection, which is the owning entity
  type. This is the same resolution rule used by ADR-0017 for single inverse refs, and the
  same rule the owning side uses for `[Owning]`.
- At `HydrateAsync` step 6: if `inverseLoader` is non-null, call
  `LoadInverseCollectionIrisAsync<TOwner>(entityIri, predicateIri)` via a cached
  `MethodInfo` and populate the collection's `_byIri` backing field via the existing
  `AddStubToCollection` helper.

### `InMemoryEntityStore.LoadInverseCollectionIrisAsync`

Scan all triples with the given (absolute) predicate; for each, check whether `targetIri`
appears directly as the object or inside an `rdf:List` chain (reusing `ListOrDirectContains`).
Yield every matching subject IRI.

### `GraphDbEntityStore.LoadInverseCollectionIrisAsync`

Same SPARQL pattern as `LoadInverseRefIriAsync` (ADR-0017) but without `LIMIT 1`, yielding
all matching owners:

```sparql
SELECT ?owner WHERE {
  { ?owner <predicate> <targetIri> }
  UNION
  { ?owner <predicate> ?list .
    ?list <rdf:rest>* ?node .
    ?node <rdf:first> <targetIri> . }
}
```

### Predicate-naming convention fix

`[Inverse]` must declare the **owning side's predicate** — the same string that appears on
the `[Owning("…")]` property on the counterpart entity. Inventing a logical reverse label
(`"isProducedBy"` instead of `"hasGenre"`) prevents any store-backed lookup because no
such triple exists. `Genre.ProducedBy` is corrected in this commit; the correct pattern was
already demonstrated by `Recording.ProducedBy` which correctly declared `"hasRecording"`.

## Consequences

- `IRdfMapper<T>.HydrateAsync` now performs one additional store round-trip per
  `[Inverse]` collection property per loaded entity. For the current workload (small graphs,
  few inverse collections) this is acceptable.
- Delegating stores (`GuardedTransactionalStore`, `AspectEnforcingEntityStore`,
  `AspectEnforcingTransactionalStore`) do **not** need to be updated: the `IInverseRefLoader`
  is passed directly from the leaf store's own `LoadAsync`, bypassing the session-loader
  cast chain entirely.
- Generator and `DeferredEntityRefCollectionImpl` machinery are unaffected.
- The `[Inverse]` `Predicate` naming convention is now unambiguously: use the owning
  side's predicate string exactly (e.g., `"hasGenre"` on `Genre.ProducedBy` because
  `Studio.Genres` declares `[Owning("hasGenre")]`; `"hasTrack"` on a `ContainedIn`
  collection because `Album.Tracks` declares `[Owning("hasTrack")]`).
