# 0004 — Lazy references via awaitable `EntityRef<T>` + ambient `EntitySession`

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Entities must be cheap to construct from a known IRI without forcing the loading of every neighbor. The user-facing API must be lean — the original sketch was `Bar bar = await foo.Bar`. We also need a clear way to distinguish "absent in store" from "not loaded yet".

## Options

1. **`EntityRef<T>` (awaitable, tri-state Unloaded/Loaded/LoadedNull) + ambient `EntitySession` (`AsyncLocal`).**
2. Constructor-injected loader on every entity. Pro: explicit. Con: noisy (`new Bar(iri, loader)`); breaks deserialization.
3. Static service locator. Pro: trivial. Con: untestable, mutable global state.
4. Per-call explicit loader (`await foo.Bar.LoadAsync(loader)`). Pro: explicit. Con: kills the lean syntax.

## Decision

Option 1.
- `EntityRef<T>` is a sealed class so caches survive copies. It implements `GetAwaiter()` and resolves via `EntitySession.RequireLoader()`.
- States: `Unloaded`, `Loaded(value)`, `Loaded(null)` (target known absent in the store).
- `EntitySession` is an `AsyncLocal` scope opened with `using var s = EntitySession.Begin(loader);`. Outside any session, awaiting an unloaded ref throws.
- Owning collections are `IEntityCollection<T>` with async enumeration; `EntityCollection<T>` is the default in-memory implementation that the generator wires with inverse-sync hooks.

## Consequences

- DI middleware in production opens a session per request.
- Tests use `InMemoryEntityLoader` inside `using var s = EntitySession.Begin(...)`.
- Loaded references stay accessible after the session closes (the value is cached in the `EntityRef<T>`).
- Cross-session sharing of a single entity instance is allowed; only resolution requires a session.
