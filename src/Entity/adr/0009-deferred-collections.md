# ADR-0009 — Deferred (Lazy) Collections via `[Owning/Inverse(Lazy = true)]`

**Date**: 2026-04-29  
**Status**: Accepted

## Context

`EntityRefCollectionImpl<T>` assumes the IRI list is pre-populated at hydration time (state: *IRIs known, objects not yet loaded*). Two gaps exist:

1. **True lazy collections** — on a freshly constructed entity, or when hydrating from a store that supports on-demand relation traversal, you do not want to enumerate all IRIs upfront. The collection should be completely unresolved until first access.
2. **Inverse collections in m:n** — the passive side (e.g. `Tag.Authors`) has no owning side to pre-populate it; it must query the store when accessed.

## Decision

### New `ICollectionLoader` interface

```csharp
public interface ICollectionLoader
{
    IAsyncEnumerable<string> LoadCollectionIrisAsync<T>(
        string ownerIri, string predicate, CancellationToken ct = default)
        where T : class, IEntity;
}
```

Implemented alongside `IEntityLoader` by stores that support relation traversal. The deferred collection casts `EntitySession.Current.Loader` to this interface at resolution time; if it is absent a clear `InvalidOperationException` is thrown.

### `DeferredEntityRefCollectionImpl<T>`

A second concrete implementation of `EntityRefCollection<T>` that holds:
- `Func<string> ownerIriSelector` — evaluated lazily so the owner's IRI is only read after it has been sealed
- `string predicate`
- optional `onAdd`/`onRemove` hooks (same as the eager impl)

On first `AddAsync`, `RemoveAsync`, `ContainsAsync`, or enumeration the collection calls `ICollectionLoader.LoadCollectionIrisAsync`, populates the IRI map, and flips `IsResolved = true`. Subsequent calls skip the load.

If no session is active the collection resolves empty (correct for new-entity construction).

### `EntityRefCollection<T>` interface additions

Two members added to the interface:

| Member | Purpose |
|--------|---------|
| `bool IsResolved` | `true` once the IRI list is populated; always `true` on the eager impl |
| `ValueTask EnsureLoadedAsync(CancellationToken)` | Explicit trigger; no-op on the eager impl |

### Annotation

```csharp
// Owning side
[Owning("hasTag", Lazy = true)]
public partial EntityRefCollection<Tag> Tags { get; }

// Inverse side (m:n)
[Inverse(nameof(Author.Tags), "isTagOf", Lazy = true)]
public partial EntityRefCollection<Author> Authors { get; }
```

Both `OwningAttribute.Lazy` and `InverseAttribute.Lazy` accept the flag (only meaningful on collection-typed properties).

### Generator behaviour

When `Lazy = true` the `__Forge_Build_X()` factory (owning) or the inline `??=` initializer (inverse) emits `DeferredEntityRefCollectionImpl<T>` with `ownerIriSelector: () => Iri` and the resolved predicate string. The `__Forge_AddTo_X` helper on the inverse side also uses `DeferredEntityRefCollectionImpl` so that a first-time `onAdd` push does not clobber existing store items.

## Alternatives Considered

- **Single unified impl with a nullable resolver thunk** — rejected; mixing the two paths in one class makes the common case (eager) pay for nullable checks on every operation.
- **Auto-detect laziness from `ICollectionLoader` presence at runtime** — rejected; explicit annotation makes the intent visible in the domain model and avoids surprise query behaviour.
- **`IQueryable`-style projection** — deferred to a future ADR; the current scope is IRI-level lazy load only.

## Consequences

- Stores implementing only `IEntityLoader` are unaffected; deferred collections on such a store throw clearly at first access.
- `EntityRefCollection<T>` grows two interface members; the eager `EntityRefCollectionImpl<T>` implements both trivially (`IsResolved = true`, `EnsureLoadedAsync` is a no-op).
- `InMemoryEntityLoader` in tests implements `ICollectionLoader` via `RegisterCollection(ownerIri, predicate, iris[])`.
