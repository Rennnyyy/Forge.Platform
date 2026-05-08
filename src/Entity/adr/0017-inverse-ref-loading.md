# 0017 — Inverse single-ref loading via `IInverseRefLoader`

- **Status**: accepted
- **Date**: 2026-05-08
- **Author**: agent

## Context

ADR-0013 deferred hydration of `[Inverse]`-annotated properties: the `ReflectionRdfMapper<T>`
skipped all `[Inverse]` properties at load time and only used the owning side as the source of
truth for graph reads.

Inverse single refs (`EntityRef<T>?`) are set in memory by the generator's `__Forge_Set_X` hook
when the owning collection mutates. On a freshly materialised entity loaded from the store,
the owning side does not run, so the inverse ref remains `null` even though the owning triple
(`<studioIri> <hasRecording> <rdf:List>`) exists in the graph.

The missing capability was observable in the HTTP API: `GET /api/entities/recordings?iri=…`
returned `null` for the inverse `studio` field even though the recording was referenced by a
studio.

Inverse *collections* (`EntityRefCollection<T>`) carrying `[Inverse]` are already handled by
`DeferredEntityRefCollectionImpl<T>` when `Lazy = true` (ADR-0009). This ADR targets only
**single refs**.

## Options

1. **Add `IInverseRefLoader` to `Forge.Entity` alongside `ICollectionLoader`; implement it in
   both store backends; make `IRdfMapper<T>.Hydrate` async and accept an `IInverseRefLoader?`
   parameter; `ReflectionRdfMapper<T>` queries the loader once per inverse single ref at
   hydration time.**
   Pro: proper async; no sync-over-async hack; consistent with the existing deferred-collection
   pattern; no ambient `AsyncLocal` workaround.
   Con: `Hydrate` → `HydrateAsync` is a signature change; callers (two store implementations)
   must be updated.

2. Use `EntitySession.Current?.Loader as IInverseRefLoader` inside the synchronous `Hydrate`
   and block with `.GetAwaiter().GetResult()`.
   Con: sync-over-async; potential deadlock on runtimes with a captured `SynchronizationContext`;
   does not work in non-session contexts.

3. Leave inverse single refs deferred entirely: introduce a read-only lazy wrapper type
   `DeferredInverseEntityRef<T>` that queries the store when awaited.
   Con: introduces a new public type that diverges from `EntityRef<T>`; consumer code cannot
   tell apart a deferred ref from a normal loaded ref without type-checking.

## Decision

Option 1.

### `IInverseRefLoader` (Forge.Entity)

```csharp
public interface IInverseRefLoader
{
    /// <summary>
    /// Returns the IRI of the single entity that points to <paramref name="targetIri"/>
    /// via <paramref name="predicate"/> (absolute IRI). Returns <see langword="null"/> if
    /// no such entity exists in the store.
    /// </summary>
    ValueTask<string?> LoadInverseRefIriAsync(
        string targetIri,
        string predicate,
        CancellationToken cancellationToken = default);
}
```

### `IRdfMapper<T>` — `HydrateAsync`

`T? Hydrate(string iri, RdfGraph subjectGraph)` is replaced by:

```csharp
ValueTask<T?> HydrateAsync(
    string iri,
    RdfGraph subjectGraph,
    IInverseRefLoader? inverseLoader = null,
    CancellationToken cancellationToken = default);
```

### `ReflectionRdfMapper<T>` additions

`TypePlan` gains an `IReadOnlyList<InverseRefProp> InverseSingleRefs`.

For each `[Inverse]` property whose CLR type is `EntityRef<T>?` (kind `InverseSingle`):
- Resolve the declared `Predicate` string to an absolute IRI.
- Locate the generator-emitted backing field `__forge_inv_{PropertyName}` via reflection.
- At `HydrateAsync` time: if `inverseLoader` is non-null, call
  `LoadInverseRefIriAsync(entityIri, predicateIri)` and, if an owner IRI is returned, create
  `EntityRef<TOwner>.ForIri(ownerIri)` and write it into the backing field.

If `inverseLoader` is null (mapper called without access to a store), inverse single refs remain
`null`. This preserves the existing in-memory construction behaviour.

### `InMemoryEntityStore.LoadInverseRefIriAsync`

- Resolve `predicate` (already absolute on entry).
- Scan all triples with that predicate for each subject: check if `targetIri` appears either as
  a direct object OR inside an `rdf:List` reachable from the predicate object.
- Return the first matching subject IRI; return `null` if none found.
- O(subjects × list-length); acceptable for the small in-memory graphs.

### `GraphDbEntityStore.LoadInverseRefIriAsync`

SPARQL query covering both direct references and `rdf:List` membership:

```sparql
SELECT ?owner WHERE {
  { ?owner <predicate> <targetIri> }
  UNION
  { ?owner <predicate> ?list .
    ?list <rdf:rest>* ?node .
    ?node <rdf:first> <targetIri> . }
}
LIMIT 1
```

### Backward reach

Only `[Inverse]` single refs are affected by this ADR. Inverse *collections* continue to be
handled by `DeferredEntityRefCollectionImpl<T>` unchanged.

## Consequences

- `IRdfMapper<T>` is a breaking change within the platform: both store implementations are
  updated in the same commit; no external consumers exist for this interface version.
- The inverse ref is populated only when a store-backed `LoadAsync` call is in flight (i.e.
  `inverseLoader` is provided). Direct mapper calls without a loader leave the ref null, which
  is correct for new-entity construction.
- `ReflectionRdfMapper<T>` SPARQL or graph costs grow by one query per inverse single ref per
  loaded entity. For the typical workload (one store round-trip per page in the API) this is
  acceptable.
- `__forge_inv_{PropertyName}` field naming remains an internal contract between
  `Forge.Entity.Generators` and `Forge.Repository`, extending the contract established in
  ADR-0013.
