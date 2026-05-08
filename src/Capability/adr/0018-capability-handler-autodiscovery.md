# 0018 — Assembly-scan autodiscovery for capability handler registration

- **Status**: superseded by [0011](0011-assembly-scanning-registration.md)
- **Date**: 2026-05-04
- **Author**: agent

## Context

`AddCapabilityHandler<TCommand, TResponse, THandler>()` requires a single explicit call per
handler. In an application with many capabilities this creates boilerplate that is purely
mechanical: the developer must enumerate every handler type at startup, and any newly
added handler is silently invisible to the dispatcher until the registration call is added.

The pattern is established across the .NET ecosystem (MediatR, MassTransit, Scrutor):
scan one or more assemblies for all concrete types that implement a known interface,
register each one automatically. The question is where in the Capability slice this scan
belongs and what the exact API surface should be.

Three design questions:

1. **Entry point** — should autodiscovery be a new overload on
   `CapabilityServiceCollectionExtensions`, or a separate class?
2. **Assembly specification** — by explicit `Assembly` reference, by a marker type whose
   assembly is inferred, or by scanning the entire entry-point assembly automatically?
3. **Duplicate handling** — should registering the same command type twice (from two
   different assemblies) throw at scan time, or defer to the HTTP-layer check at
   `MapCapabilities()`?

## Options

### Entry point

1. **New overloads on the existing `CapabilityServiceCollectionExtensions`** — keeps all
   DI registration in one class; consistent with the existing extension method pattern.
2. A separate `CapabilityHandlerScanner` class.
   Con: adds a public type with no conceptual difference from the extension method;
   harder to discover.

### Assembly specification

1. **`AddCapabilityHandlers(params Assembly[] assemblies)` + convenience
   `AddCapabilityHandlersFromAssemblyContaining<T>()`** — the developer controls exactly
   which assemblies are scanned; no implicit magic. The generic helper eliminates the
   `typeof(T).Assembly` ceremony.
2. Auto-detect entry assembly via `Assembly.GetEntryAssembly()`. Pro: zero ceremony.
   Con: breaks in test hosts that have no entry assembly; scanning third-party assemblies
   unintentionally; unclear surface.
3. Accept a `Type` directly and scan only that type's assembly.
   Pro: concise. Con: less flexible; can't span two assemblies.

### Duplicate handling

1. **`TryAdd` semantics — first registration wins; no exception at scan time.**
   The HTTP layer already throws on duplicate command types at `MapCapabilities()` time,
   which is the right enforcement point. At registration time, silently skipping duplicates
   prevents ordering issues when calling `AddCapabilityHandlers` from multiple assemblies
   that happen to share a base assembly.
2. Throw at scan time on duplicate command type.
   Con: `TryAdd` semantics everywhere else in DI; inconsistent. HTTP layer already has the
   guard — no need for a second enforcement point.

## Decision

- **Entry point**: Option 1 — new overloads on `CapabilityServiceCollectionExtensions`.
- **Assembly specification**: Option 1 — `AddCapabilityHandlers(params Assembly[])` +
  `AddCapabilityHandlersFromAssemblyContaining<T>()`.
- **Duplicate handling**: Option 1 — `TryAdd` semantics; HTTP layer enforces uniqueness.

### API surface added to `CapabilityServiceCollectionExtensions`

```csharp
/// <summary>
/// Scans the supplied assemblies for all concrete types that implement
/// ICapabilityHandler&lt;TCommand, TResponse&gt; and registers each pair.
/// Uses TryAdd semantics — a type already registered manually is not overwritten.
/// </summary>
public static IServiceCollection AddCapabilityHandlers(
    this IServiceCollection services,
    params Assembly[] assemblies);

/// <summary>
/// Convenience overload — scans the assembly that contains <typeparamref name="T"/>.
/// </summary>
public static IServiceCollection AddCapabilityHandlersFromAssemblyContaining<T>(
    this IServiceCollection services);
```

### Implementation detail

The generic `AddCapabilityHandler<TCommand, TResponse, THandler>` is refactored to
delegate to a private `AddCapabilityHandlerCore(IServiceCollection, Type, Type, Type)`
helper so the same registration logic is shared with the scan path. Scan code uses
`assembly.GetTypes()` to include both public and internal handler implementations.

A handler class that implements `ICapabilityHandler<TCommand, TResponse>` multiple times
with different type parameters is registered once per interface.

## Changes

| Action | File |
|--------|------|
| Modify | `src/Capability/DependencyInjection/CapabilityServiceCollectionExtensions.cs` |
| Create | `tests/Capability.Tests/AddCapabilityHandlersTests.cs` |
| Modify | `src/Capability/adr/README.md` — index entry |
| Modify | `samples/Capability.Http.Sample/Program.cs` — use autodiscovery |

## Consequences

- Adding a new capability handler no longer requires a manual registration call;
  the scan call at startup covers all handlers in the specified assemblies.
- Explicit `AddCapabilityHandler<>()` calls remain fully supported and are preferred
  for fine-grained control (e.g. registering only a subset of handlers from an assembly,
  or registering a handler whose assembly is not scanned).
- `TryAdd` semantics mean an explicit `AddCapabilityHandler<>()` registered before
  `AddCapabilityHandlers()` is preserved; the scan does not overwrite it.
- Duplicate command types (two handlers for the same `TCommand`) continue to surface at
  `MapCapabilities()` via the existing guard in Capability.Http ADR-0002.
- `assembly.GetTypes()` is used rather than `GetExportedTypes()` so that internal handler
  classes (common in application assemblies) are also discovered.
