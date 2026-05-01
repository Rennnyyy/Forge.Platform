# 0010 — `IEntityOptions` interface, ambient override, and `EntityOptionsInstance`

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: agent

## Context

`EntityOptions` is a static class with mutable global fields. That is intentional for test ergonomics
(`EntityOptions.BaseIri = "https://forge-it.net"` before a test run), but it makes per-request
configuration impossible in multi-tenant DI scenarios, and it provides no injection point for
env-variable / `appsettings.json` binding (`Microsoft.Extensions.Options`).

## Options

1. **Keep static class; add `IEntityOptions` interface + `EntityOptionsInstance` concrete class +
   `EntityOptions.Current` ambient override via `AsyncLocal<IEntityOptions?>`.**
   Static setters remain for backward-compatible test usage; DI consumers register an
   `IEntityOptions` and activate it per async-flow with `EntityOptions.Use(options)`.
2. Replace the static class entirely with an instance-based approach registered in DI.
   Pro: clean DI. Con: breaks all existing test code; generated code can no longer call
   `EntityOptions.BaseIri` without a compile break.
3. Expose a static `IServiceProvider` on `EntityOptions` so the generated code can resolve
   `IEntityOptions` at runtime. Pro: familiar pattern. Con: service-locator anti-pattern;
   mutable global, untestable.

## Decision

Option 1.

### `IEntityOptions`

Read-only interface:
```csharp
public interface IEntityOptions
{
    string BaseIri { get; }
    string PredicateBaseIri { get; }
}
```

### `EntityOptionsInstance`

Mutable POCO implementing `IEntityOptions`. Used directly in DI:
- `services.Configure<EntityOptionsInstance>(config.GetSection("Entity"))`
- `services.AddSingleton<IEntityOptions, EntityOptionsInstance>()`

### `EntityOptions.Current`

A new static `IEntityOptions Current` property returns the `AsyncLocal<IEntityOptions?>` override
if one is active (set via `EntityOptions.Use(options)`), otherwise falls back to a private
`StaticEntityOptions` adapter that reads the existing `_baseIri` / `_predicateBaseIri` static
backing fields.

### `EntityOptions.Use(IEntityOptions)`

Returns an `IDisposable` scope that sets the ambient options for the current async control flow
and restores the previous value on `Dispose`. Typical DI middleware pattern:
```csharp
using var _ = EntityOptions.Use(injectedOptions);
await next(context);
```

### Generator change

The emitter is updated to reference `global::Forge.Entity.EntityOptions.Current.BaseIri`
instead of `global::Forge.Entity.EntityOptions.BaseIri` so it participates in the ambient override.

## Consequences

- All existing tests that set `EntityOptions.BaseIri = "..."` continue to compile and pass unchanged.
- DI consumers can configure options per tenant / per request scope.
- A future ADR can add env-var / `IOptions<T>` binding by providing a class implementing `IEntityOptions`
  without any change to the core library.
- The ambient is `AsyncLocal`, so the scope is correctly inherited by child async flows and does not
  leak across unrelated concurrent requests.
