# 0004 — VariantFilteringStore: IEntityStore decorator for QueryByTypeAsync<Usage>

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

`VariantScope` (ADR-0003) provides an ambient `VariantConfiguration` for the current
async control flow. A caller who opens a scope and then calls
`IEntityStore.QueryByTypeAsync<Usage>()` expects to receive only the `Usage` entities
whose `ConditionSet` is satisfied by the active configuration. No mechanism exists today
to enforce that filter transparently — every caller would have to remember to apply it
by hand.

The established solution for transparent per-request store behaviour in this codebase
is the `IEntityStore` decorator pattern (see `AspectEnforcingEntityStore` in
`Forge.Aspects`). A new `VariantFilteringStore` decorator intercepts
`QueryByTypeAsync<Usage>` and applies the ambient `VariantScope.Current` as a post-load
C# filter. All other `IEntityStore` methods delegate to the inner store unchanged.

## Options

1. **`VariantFilteringStore` decorator on `IEntityStore`.** Follows the exact pattern
   established by `AspectEnforcingEntityStore`. Intercepts only
   `QueryByTypeAsync<T>` when `T == typeof(Usage)` and a scope is active.
   All other methods and interfaces are pure delegation.
   Pro: transparent to callers; composition over inheritance; no interface changes.

2. **Caller helper — e.g. `VariantScope.FilterUsages(IAsyncEnumerable<Usage>)`.** The
   store is untouched; callers call a helper after querying.
   Con: callers can forget; no single place to enforce the contract.

3. **SPARQL-based server-side filtering.** Emit a SPARQL `FILTER` clause to exclude
   non-satisfied `Usage` triples before they are fetched.
   Con: `ConditionSet` is not persisted by the default mapper (ADR-0001); this approach
   is a deferred v2 concern noted in ADR-0002.

## Decision

Option 1 — `VariantFilteringStore`.

### Filter logic

```
When T == typeof(Usage) AND VariantScope.Current is not null:
  Iterate inner.QueryByTypeAsync<Usage>()
  Yield only those where usage.Conditions.IsSatisfiedBy(VariantScope.Current)

Otherwise:
  Return inner.QueryByTypeAsync<T>() unfiltered
```

`VariantScope` is read **at iteration time** (not at the method call site), because the
scope may have been opened before `QueryByTypeAsync` is called and the configuration
must propagate correctly into async iterators that resume on different threads. The
`AsyncLocal<T>` `ExecutionContext` captures this correctly.

### DI wiring — `AddForgeVariant()`

The DI extension method follows the pattern from `AddForgeAspects()` (ADR-0014):

1. The current unkeyed `IEntityStore` descriptor (if any) is captured at registration
   time and removed from the collection.
2. A new unkeyed `IEntityStore` singleton factory registers a `VariantFilteringStore`
   wrapping the captured descriptor (resolved at provider-build time).
3. If no unkeyed `IEntityStore` exists at registration time, the factory falls back to
   the well-known `ForgeEntityRepositoryBuilder.BackendStoreKey` keyed service.
4. `AddForgeVariant()` is documented to be called **after** `AddForgeAspects()` (when
   both are used), so the decorator chain is: VariantFiltering → AspectEnforcing →
   Backend. This ordering means the most-specific filter (variant) is outermost and
   does not restrict the aspect engine from running its access-gate queries against the
   full backend.

### `Forge.Variant.csproj` dependency

The new `VariantFilteringStore` and its DI extension require `Forge.Repository` as a
direct project reference. This dependency was deferred in ADR-0001; this ADR accepts it.

## Consequences

- Callers who open a `VariantScope` and call `QueryByTypeAsync<Usage>()` automatically
  receive only satisfied `Usage` edges without any additional code.
- The decorator is a pure pass-through for all non-`Usage` types and when no scope is
  active — zero performance impact outside of variant-aware contexts.
- `Forge.Variant` gains a compile-time dependency on `Forge.Repository`.
- If `AddForgeVariant()` is called before `AddForgeAspects()` (inverted order), the
  aspect wrapper becomes the outermost decorator and variant filtering happens below it.
  This produces correct results (both filters are applied) but suboptimal ordering
  (aspect access gate runs on all Usages before variant filtering reduces the set).
  The DI helper includes a doc-comment warning about recommended ordering.
