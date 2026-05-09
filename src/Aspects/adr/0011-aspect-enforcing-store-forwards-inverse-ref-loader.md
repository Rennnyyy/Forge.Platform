# 0011 — `AspectEnforcingEntityStore` implements `IInverseRefLoader`

- **Status**: accepted
- **Date**: 2026-05-09
- **Author**: agent

## Context

`IInverseRefLoader` is implemented directly by the concrete backend stores
(`InMemoryEntityStore`, `GraphDbEntityStore`). The HTTP read endpoint for enumeration
types detects the interface at runtime to hydrate `[Inverse]` collections:

```csharp
if (mapper is not null && store is IInverseRefLoader inverseLoader)
{
    // ... HydrateAsync with inverseLoader ...
}
```

`IEntityStore` is resolved from DI. When `AddForgeAspects()` is active,
the DI-registered `IEntityStore` is `AspectEnforcingEntityStore` — a decorator that
wraps the raw backend. `AspectEnforcingEntityStore` did not implement `IInverseRefLoader`,
so `store is IInverseRefLoader` always evaluates to `false` in decorated stacks.

The result: `[Inverse]` collection properties (e.g. `Genre.ProducedBy`) were always
empty in single-read HTTP responses whenever the Aspects slice was active, even when
the backend store contained the correct data. The unit tests for the stores passed
because they test the backend directly, without the decorator.

## Options

1. **Forward `IInverseRefLoader` through `AspectEnforcingEntityStore`.**
   Add `IInverseRefLoader` to the implemented interface list; delegate both methods —
   `LoadInverseRefIriAsync` and `LoadInverseCollectionIrisAsync<T>` — to the inner store
   if it implements `IInverseRefLoader`, returning `null` / empty stream otherwise.

2. **Change the endpoint to resolve `IInverseRefLoader` from DI as a separate service.**
   Pro: avoids interface proliferation on the decorator. Con: requires backends to register
   `IInverseRefLoader` as a separate DI entry; fragile if backends diverge.

3. **Do the lookup via `IServiceProvider` to unwrap the decorator.**
   Con: tight coupling to the container; breaks the decorator abstraction entirely.

## Decision

Option 1 — mirrors the existing `ICollectionLoader` forwarding already present in the
same class:

```csharp
internal sealed class AspectEnforcingEntityStore
    : IEntityStore, ISparqlQueryStore, IInverseRefLoader
{
    ValueTask<string?> IInverseRefLoader.LoadInverseRefIriAsync(...)
        => _inner is IInverseRefLoader il
            ? il.LoadInverseRefIriAsync(...) : ValueTask.FromResult<string?>(null);

    IAsyncEnumerable<string> IInverseRefLoader.LoadInverseCollectionIrisAsync<T>(...)
        => _inner is IInverseRefLoader il
            ? il.LoadInverseCollectionIrisAsync<T>(...) : AsyncEnumerable.Empty<string>();
}
```

The same pattern should be applied to any future decorator that wraps an `IEntityStore`.

## Consequences

- `[Inverse]` single-ref and collection properties are hydrated correctly in HTTP
  single-read responses even when the Aspects slice is active.
- `AspectEnforcingTransactionalStore` does not need the same treatment because the
  HTTP endpoint resolves the unkeyed `IEntityStore` (not `ITransactionalEntityStore`)
  for this path.
- New decorators wrapping `IEntityStore` must document whether they forward
  `IInverseRefLoader`; SLICING.md should be updated to note this expectation.
