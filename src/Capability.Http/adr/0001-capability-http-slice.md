# 0001 — Capability.Http slice scope and public surface

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

`Forge.Capability` defines the dispatcher pipeline and the handler contract but carries no
ASP.NET Core dependency. A dedicated slice is needed to expose capabilities as Minimal API
HTTP endpoints without polluting the core with transport-specific concerns. The precedent is
`Forge.Repository.InMemory` and `Forge.Repository.GraphDb` as transport satellites of
`Forge.Repository`.

## Options

1. **New `Forge.Capability.Http` slice** with three public surface areas:
   `IEndpointRouteBuilder.MapCapabilities()`, `ICapabilityAspectIriProvider`, and
   `IServiceCollection.AddCapabilityHttp()`. Starts flat (≤ 10 files; ADR-0010 threshold).
2. Extend `Forge.Capability` directly.
   Con: forces `Microsoft.AspNetCore.App` onto every consumer including in-process and
   messaging-only contexts.
3. Use a source generator to emit endpoint registration code.
   Con: significant complexity; no current requirement beyond minimal API registration.

## Decision

Option 1.

### Dependency graph

```
Forge.Capability.Http
  → Forge.Capability
  → Microsoft.AspNetCore.App (FrameworkReference)
```

`Forge.Authorization.Http` is an independent slice used by applications alongside
`Forge.Capability.Http` but not a direct compile-time dependency of it (the agent-token
middleware is an application-level concern, not a capability-routing concern).

### Public surface

| Member | Purpose |
|--------|---------|
| `ICapabilityAspectIriProvider` | Seam: resolves the capability-aspect IRI for the current HTTP request |
| `HeaderCapabilityAspectIriProvider` | Default implementation: reads `X-Forge-Capability-AspectIri` |
| `IEndpointRouteBuilder.MapCapabilities()` | Scans DI for registered handlers and registers one Minimal API endpoint per handler |
| `IServiceCollection.AddCapabilityHttp()` | Registers default provider; scans existing handler registrations to build endpoint metadata |

### `AddCapabilityHttp()` must be called after all `AddCapabilityHandler<>()` calls

`AddCapabilityHttp()` scans the `IServiceCollection` snapshot at call time to discover
`ICapabilityHandler<,>` registrations and build handler descriptor entries used by
`MapCapabilities()`. Any handler registered after `AddCapabilityHttp()` will not be
auto-discovered.

## Consequences

- `Forge.Capability` remains usable in pure in-process and future messaging contexts.
- Applications opt into HTTP exposure per-host; the core assembly is unchanged.
- Adding a new capability handler automatically exposes it via HTTP by re-calling
  `AddCapabilityHttp()` after the new `AddCapabilityHandler<>()` — no manual
  endpoint registration required.
