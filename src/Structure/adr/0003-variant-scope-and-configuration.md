# 0003 — VariantScope and VariantConfiguration

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

The configured-tree capability and any store decorator that perform variant-based
filtering need access to a per-request `VariantConfiguration` value. The existing
`IAspectStore` (Aspects slice, ADR-0007) is sealed at startup and cannot carry
runtime-dynamic values: it stores baked-in SPARQL strings registered during DI
composition, not per-call configuration records.

A parallel mechanism is needed that follows the same `AsyncLocal<T>` ambient-scope
pattern as `QueryAspectScope` in `Forge.Aspects`, but carries a typed record instead
of an IRI string.

## Options

1. **`VariantScope` static class with `AsyncLocal<VariantConfiguration?>`.**
   Mirrors `QueryAspectScope` exactly: a static `Use(VariantConfiguration)` method
   captures the previous value, sets the new one, and returns an `IDisposable` scope
   that restores the previous value on disposal. Reading is via `VariantScope.Current`.
   Pro: identical usage pattern; no DI complexity; composable with any async flow.
2. **`IVariantConfigurationAccessor` DI interface (`IHttpContextAccessor`-style).**
   Per-request scoped service. Con: ties the mechanism to DI lifetime; complicates
   non-HTTP callers (CLI, tests, messaging consumers) that do not have request scopes.
3. **Pass `VariantConfiguration` as an explicit parameter on every method.**
   Pro: fully explicit. Con: invasive to the existing `IEntityStore` interface contract;
   cannot be retrofitted to the existing store chain without API-breaking changes.

## Decision

Option 1 — `VariantScope` static class.

### `VariantConfiguration`

Immutable record:

```csharp
public sealed record VariantConfiguration(
    string BranchIri,
    IReadOnlyDictionary<string, VariantValue> Options,
    DateTimeOffset? ReferenceDate = null);
```

- `BranchIri` — IRI of the named graph (branch) the tree is read from.
  Empty string selects the default branch (caller responsibility).
- `Options` — maps dimension IRI → selected `VariantValue`; may be empty.
- `ReferenceDate` — evaluation instant for `MilestoneCondition`; null = `DateTimeOffset.UtcNow`.

### `VariantScope`

```csharp
public static class VariantScope
{
    public static VariantConfiguration? Current { get; }
    public static IDisposable Use(VariantConfiguration configuration);
}
```

Nested scopes are supported: `Use` captures the previous value; `Dispose` restores it.
Callers do not need to guard against null at the call site — they open a scope before
entering any variant-filtered operation. Consumer code (future `VariantFilteringStore`)
checks `VariantScope.Current` and skips filtering when null (no scope = unfiltered).

### Relationship to `QueryAspectScope`

`VariantScope` is intentionally parallel to, not derived from, `QueryAspectScope`. The
two scopes are independent and composable:

```csharp
using var _aspect = QueryAspectScope.Use(aspectIri);
using var _variant = VariantScope.Use(config);
// Both active simultaneously
```

## Consequences

- Any async flow (test, HTTP middleware, messaging consumer) can open a `VariantScope`
  without DI plumbing.
- The `VariantFilteringStore` decorator (future task) reads `VariantScope.Current` to
  decide whether to filter `QueryByTypeAsync<Usage>` results.
- Closures capturing `VariantScope.Current` before entering an async yield point work
  correctly because `AsyncLocal<T>` flows with the `ExecutionContext`.
- A `VariantScope` that is never opened means "all Usages pass" — safe for contexts
  where variant configuration is not relevant (e.g. management graph writes).
