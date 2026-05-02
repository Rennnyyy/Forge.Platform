# 0001 — Active-record CRUD operations on generated entities via ambient IEntityStore

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

The Repository slice (ADR-0005, ADR-0013) exposes CRUD via `IEntityRepository<T>` — a typed
facade that callers must obtain from DI or construct manually. Application code that simply
wants to persist a freshly created entity still has to carry a reference to the repository
object alongside the entity itself.

A second ergonomic layer is needed: methods directly on each entity class that route to the
configured store without the caller holding a repository reference.

## Options

1. **Active-record methods emitted onto each entity by a source generator; a static ambient
   `EntityOperations` class (modelled after `EntityOptions`) holds the `IEntityStore` for the
   current async control flow.**
   - Instance methods: `CreateAsync`, `UpdateAsync`, `DeleteAsync`.
   - Static methods: `ReadAsync(iri)`, `ListAsync()`.
   - `EntityOperations.Use(store)` opens a scope (any `IEntityStore` backend).
   - `EntityOperations.RequireStore()` is the single call site from generated code; throws
     with a clear message if no store is bound.
2. Extension methods on `IEntity`. Pro: no generator needed. Con: extension methods cannot be
   static on the generic instance, so `Foo.ReadAsync(iri)` would not be expressible.
3. Extend `EntityBase` with abstract CRUD methods. Pro: enforced by inheritance. Con: `EntityBase`
   (in core `Forge.Entity`) must not depend on the Repository slice; adding persistence to core
   violates the slice-per-concern layout (ADR-0005).

## Decision

Option 1.

- New slice: `src/Operations/` → `Forge.Operations`.
- New generator slice: `src/Operations.Generators/` → `Forge.Operations.Generators`
  (see ADR-0002).
- The ambient store is bound via `EntityOperations.Use(IEntityStore)`, which returns an
  `IDisposable` scope backed by `AsyncLocal<IEntityStore?>`. The pattern mirrors
  `EntityOptions.Use(IEntityOptions)` (Entity ADR-0010).
- `EntityOperations` exposes typed delegation helpers (`CreateAsync<T>`, `UpdateAsync<T>`,
  `DeleteAsync`, `ReadAsync<T>`, `ListAsync<T>`) called by the generated methods; this keeps
  the emitted code free of references to `Forge.Repository` types directly.
- The slice depends on `Forge.Repository` for `IEntityStore` and `WriteMode`.
  Consumers that opt in to this slice transitively obtain the Repository abstractions layer.

## Consequences

- Entity CRUD becomes: `await artist.CreateAsync()`, `await Artist.ReadAsync(iri)`, etc.
- No DI container is required; any `IEntityStore` backend works with `Use(store)`.
- For DI / per-request wiring a host-level scope (e.g. ASP.NET Core middleware calling
  `EntityOperations.Use(resolvedStore)`) is sufficient — no additional DI extension is needed
  in this slice for v1.
- `CreateAsync` maps to `WriteMode.Create`; `UpdateAsync` maps to `WriteMode.Replace`.
  Callers that need other `WriteMode` combinations should use `IEntityRepository<T>` directly.
