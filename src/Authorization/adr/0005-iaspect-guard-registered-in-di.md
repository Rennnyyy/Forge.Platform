# 0005 — Register `IAspectGuard` in the DI container

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent (pending user acceptance)

## Context

`CapabilityDispatcher` accepted `IAspectGuard?` (nullable) and fell back to
`AllowAllAspectGuard.Instance` in its constructor when `null` was injected.
`CapabilityServiceCollectionExtensions.AddCapabilityHandlerCore` resolved the
guard via `sp.GetService<IAspectGuard>()`, which silently returns `null` when no
guard has been registered.

`AddForgeAuthorization` similarly accepted the guard as a direct parameter and
wired it into `GuardedTransactionalStore` without persisting it in the DI
container.

The result is that `IAspectGuard` never appeared in the DI container. This means:

- There is no startup-time failure if a host forgets to register a guard.
- Diagnostics tooling (health checks, `dotnet-counters`, custom startup validators)
  cannot discover the active guard type via the service collection.
- The default fallback is invisible; developers cannot tell that `AllowAllAspectGuard`
  is in use without reading source code.

## Decision

1. **`AddCapabilityHandlerCore`** calls
   `services.TryAddSingleton<IAspectGuard>(AllowAllAspectGuard.Instance)` before
   building the dispatcher factory. This ensures `IAspectGuard` is always resolvable;
   a real guard registered prior to `AddCapabilityHandlers` takes precedence because
   `TryAdd` semantics (first registration wins).

2. **`CapabilityServiceCollectionExtensions`** switches from
   `sp.GetService<IAspectGuard>()` to `sp.GetRequiredService<IAspectGuard>()`.
   The previous `GetService` pattern hid missing registrations at startup and deferred
   the null-coalesce to `CapabilityDispatcher`'s constructor; both sites are now
   consistent.

3. **`AddForgeAuthorization`** calls
   `services.TryAddSingleton<IAspectGuard>(effectiveGuard)` immediately after
   resolving `effectiveGuard`. This makes the guard visible in the DI container when
   authorization is explicitly configured.

## Consequences

- `IAspectGuard` is always in the DI container after `AddCapabilityHandlers` or
  `AddForgeAuthorization` is called.
- The default guard (`AllowAllAspectGuard`) is now an explicit, inspectable registration
  rather than a hidden null-coalesce.
- Applications that register a custom `IAspectGuard` before calling these methods
  are not affected — `TryAdd` honours the prior registration.
- `CapabilityDispatcher`'s nullable `guard` parameter and `guard ?? AllowAllAspectGuard.Instance`
  null-coalesce remain valid defensive code; they are not removed.
