# 0004 — `EntityOperations.BeginTransaction()` as the ambient transaction entry-point

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

Entity ADR-0015 introduces `ITransactionalEntityStore` and `EntityTransaction`. Application
code that already uses `EntityOperations.Use(store)` to bind a store should be able to open
a transaction without re-resolving the store.

## Options

1. **Add `BeginTransaction()` directly on `EntityOperations`.**
   The method calls `RequireStore()`, checks for `ITransactionalEntityStore`, and returns
   a new `EntityTransaction`. Consistent with `Query<T>()` (ADR-0003) and `Use(store)`.
2. Ship as an extension method on `EntityOperations` from the Repository slice.
   Pro: keeps Operations unaware of transaction internals. Con: requires a `using` directive;
   breaks the "single ambient type, all common verbs" story.
3. Require callers to obtain the store and construct `EntityTransaction` directly.
   Pro: no extra method. Con: defeats the purpose of the ambient layer.

## Decision

Option 1.

- `EntityOperations.BeginTransaction()` throws `NotSupportedException` if the bound store
  does not implement `ITransactionalEntityStore`.
- Returns `EntityTransaction` (from `Forge.Repository`); it is `IAsyncDisposable`.

## Consequences

- Pattern: `await using var tx = EntityOperations.BeginTransaction(); tx.Create(…); await tx.CommitAsync();`
- Stores that don't implement transactions are rejected early with a clear message.
