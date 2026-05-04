# 0002 — Route derivation and capability auto-discovery

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

Requiring a manual `MapPost(route, handler)` call per capability violates the zero-ceremony
principle: endpoints will be forgotten, routes will drift from identity strings, and the
registration burden grows linearly with the number of capabilities.

## Options

1. **`app.MapCapabilities()` scans DI for `ICapabilityHandler<TCommand, TResponse>`
   registrations and reads `[Capability]` from the handler type.** Route path is derived
   via `CapabilityIdentity.ToRoutePath()`; verb is `POST` by default. Duplicate command
   types throw at startup. Handler without `[Capability]` throws at startup.
2. Explicit per-capability registration via a fluent builder. Con: repetitive; the
   handler type already carries the identity via `[Capability]`.
3. Source generator emits registration code. Con: significant complexity; no current
   requirement.

## Decision

Option 1.

### Route derivation

`CapabilityIdentity.ToRoutePath()` (Capability ADR-0010) converts dots to slashes:

| Identity | Route path |
|----------|-----------|
| `catalog.artists.create` | `catalog/artists/create` |
| `artists.v2.get` | `artists/v2/get` |

### HTTP verb

Always `POST` in this version. Verb selection is not part of `[Capability]` (Capability
ADR-0010 explicitly excludes a `Method` property to keep the attribute transport-agnostic).
A `MapCapabilities(RouteGroupBuilder group)` overload is out of scope for v1 but is the
recommended future extension point.

### Command binding

The command type is bound from the JSON request body (Minimal API default for POST with a
complex type parameter). Query-string binding is not supported in v1.

### Discovery mechanism

`AddCapabilityHttp()` scans the `IServiceCollection` snapshot at call time for descriptors
with `ServiceType == ICapabilityHandler<TCommand, TResponse>` and `ImplementationType !=
null`. It registers a `CapabilityHandlerDescriptor` (internal) for each. `MapCapabilities()`
resolves all descriptors from the `IServiceProvider` and registers one endpoint per
descriptor.

### Guard condition: duplicate command types

If two descriptors share the same `CommandType`, `MapCapabilities()` throws
`InvalidOperationException` naming both handler types.

### Guard condition: missing `[Capability]` attribute

If a handler type has no `[Capability]` attribute, `MapCapabilities()` throws
`InvalidOperationException` naming the handler type.

## Consequences

- Zero manual route registration for consumers of the platform.
- Adding a capability is: write a handler class, annotate with `[Capability]`, call
  `AddCapabilityHandler<>()` before `AddCapabilityHttp()`.
- The route-to-identity mapping is trivially auditable: `CapabilityIdentity.ToRoutePath()`
  is the sole derivation rule.
