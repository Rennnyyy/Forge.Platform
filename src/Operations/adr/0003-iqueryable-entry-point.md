# 0003 — `EntityOperations.Query<T>()` exposes an EF-Core-shaped IQueryable surface

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

ADR-0001 of this slice (active-record CRUD) pinned `EntityOperations` as the ambient
entry-point for entity persistence. The newly introduced `Forge.Sparql` slice
([Sparql ADR-0001](../../Entity.Sparql/adr/0001-linq-to-sparql-provider.md)) ships a
LINQ-to-SPARQL provider behind an `IEntityStore.Query<T>()` extension. Application code
that already uses `EntityOperations.Use(store)` to bind a store should be able to
write `EntityOperations.Query<T>().Where(...).ToListAsync()` without re-resolving the
store.

The decision is whether the `Query<T>()` entry-point lives in the Sparql slice (as an
extension on the ambient class) or directly on `EntityOperations` itself.

## Options

1. **Add `Query<T>()` directly on `EntityOperations`**, with `Forge.Operations`
   referencing `Forge.Sparql`. The method composes with the existing CRUD
   helpers (`CreateAsync` / `ReadAsync` / etc.) and is discoverable on the same type.
2. Ship `Query<T>()` as an extension method in the Sparql slice on `EntityOperations`.
   Pro: keeps Operations unaware of LINQ. Con: extension methods on a static class
   require a `using Forge.Sparql;`, which breaks the "one ambient type, all
   common verbs" discovery story of the Operations slice.
3. Require callers to obtain the store first (`EntityOperations.RequireStore().Query<T>()`).
   Pro: no slice coupling. Con: every call site repeats two-step ceremony for a verb
   that is conceptually as common as `ReadAsync` / `ListAsync`.

## Decision

Option 1.

- `Forge.Operations.csproj` adds a `ProjectReference` to
  `Forge.Sparql.csproj`.
- `EntityOperations` gains:
  ```csharp
  public static IQueryable<T> Query<T>() where T : class, IEntity;
  ```
  which delegates to `RequireStore().Query<T>()` (the Sparql slice's extension on
  `IEntityStore`).
- The method throws `NotSupportedException` (surfaced by the Sparql slice) when the
  bound store does not implement `ISparqlQueryStore`.

## Consequences

- Operations callers gain a third common verb on the same ambient type:
  `await EntityOperations.Query<Artist>().Where(a => a.Country == "us").ToListAsync()`.
- Operations now transitively exposes the Sparql slice's types
  (`AsyncQueryableExtensions`, `IOrderedQueryable<T>`). This is the intended ergonomic
  outcome, not an accidental leak.
- Back-ends that do not support SPARQL still wire into Operations for CRUD; only the
  `Query<T>()` call path errors out — and only when actually invoked.
- The `Operations.Generators` source generator is **not** modified in this change.
  Adding a per-entity `static IQueryable<T> Query()` shortcut on each generated class
  is a follow-up. The static `EntityOperations.Query<T>()` is sufficient for v1.
