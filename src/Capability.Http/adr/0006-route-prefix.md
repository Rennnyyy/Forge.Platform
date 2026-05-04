# 0006 — Route prefix: `api/capabilities/` and `api/entities/`

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0002 established that `MapCapabilities()` derives route paths by replacing dots with
slashes in the capability identity (e.g. `catalog.artists.create` →
`catalog/artists/create`). The paths are registered at the application root with no
prefix, so a capability lives directly at `/catalog/artists/create`.

As the platform grows, two groups of capabilities emerge:

- **General capabilities** — hand-written handlers wired to application-specific
  operations (greet, catalog-item management, etc.).
- **Entity CRUD capabilities** — generated handlers produced by `[CrudCapabilities]` on
  an entity class; they expose the entity's persistence surface over HTTP.

Mixing both groups at the path root creates ambiguity for API consumers and makes the
routing surface harder to understand at a glance (e.g. `POST /books/create` and
`POST /demo/greet` look like peers when they represent fundamentally different concerns).

A stable, declared URL prefix separates the two groups, making the API surface self-
describing and enabling future versioning or gateway routing to target each group
independently.

## Options

1. **Two fixed prefixes, applied in `MapCapabilities()`.**
   - All handlers: `api/capabilities/{derived-path}`.
   - Handlers carrying `[CrudCapabilityHandler]` (see Capability ADR-0013): `api/entities/{derived-path}`.
   No configuration knobs; prefixes are a platform-wide convention.
2. **Configurable prefix via `MapCapabilitiesOptions`.**
   Pro: caller flexibility. Con: per-deployment drift; tests and documentation must
   account for all possible prefix values; the zero-configuration model of `MapCapabilities()`
   is preserved by providing sensible defaults — making the defaults the only supported
   value achieves the same result without the configuration surface.
3. **Use `IEndpointRouteBuilder.MapGroup(prefix).MapCapabilities()` for caller-defined
   prefixes.** Con: ASP.NET route groups do not compose with the auto-discovery scan, which
   runs inside `MapCapabilities()` — implementing group-aware discovery requires
   significant refactoring and a new public surface.

## Decision

Option 1.

### Prefix rules

| Handler carries `[CrudCapabilityHandler]`? | Registered route |
|---------------------------------------------|-----------------|
| Yes | `api/entities/{identity.ToRoutePath()}` |
| No | `api/capabilities/{identity.ToRoutePath()}` |

`[CrudCapabilityHandler]` (declared in `Forge.Capability`) is emitted by the
`Forge.Capability.Generators` source generator on every generated handler class.
Hand-written handlers may also carry it explicitly to opt in to the entity prefix.

### Implementation

`EndpointRouteBuilderExtensions.MapCapabilities()` interprets the attribute and prepends
the appropriate prefix when building the route path passed to `app.MapMethods()`.

The guard conditions (duplicate commands, missing `[Capability]`, bodyless methods) are
unchanged.

## Consequences

- All hand-written capability endpoints move from `/{path}` to `api/capabilities/{path}`.
- All CRUD entity capability endpoints move from `/{path}` to `api/entities/{path}`.
- All consumers (Bruno collections, integration tests, clients) must be updated to the
  new paths.
- Route names and paths are now immediately distinguishable in logs and gateway rules.
- Adding a new capability continues to require zero manual route registration.
