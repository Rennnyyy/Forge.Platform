# 0010 — Restore `api/entities/` prefix for `[CrudCapabilityHandler]` handlers

- **Status**: accepted; supersedes [0009](0009-capabilities-prefix-only.md)
- **Date**: 2026-05-07
- **Author**: agent (user directive)

## Context

ADR-0009 narrowed `MapCapabilities()` to register all handlers — including those carrying
`[CrudCapabilityHandler]` — exclusively under `api/capabilities/`. The stated motivation
was to avoid a collision with `Forge.Operations.Http`'s `MapOperations()`, which claims
`api/entities/` as its route prefix.

After review this was found to be overly conservative. The two route sets are
**structurally distinct** and cannot collide on the same ASP.NET route:

| Surface | Route shape | Example |
|---------|------------|---------|
| `MapCapabilities()` CRUD | `api/entities/{entity}/{verb}` | `POST api/entities/books/create` |
| `MapOperations()` REST | `api/entities/{entity}` + HTTP verb | `POST api/entities/artists` |

A collision would require a single entity to be registered with *both* `[CrudCapabilities]`
and `[OperationEndpoints]` **and** share the same path segment. Even then the paths differ
structurally (verb-in-path vs. REST), so ASP.NET routing resolves them unambiguously.
Applications that genuinely mix both surfaces for the same entity path must not do so —
the startup guard responsible for duplicates in each surface (duplicate command types for
capabilities; the DI descriptor scan for operations) already catches the within-surface
duplicates; cross-surface overlap is an application-level design error, not a framework
problem to solve by renaming prefixes.

`api/entities/` is the natural URL space for entity data regardless of which dispatch
mechanism is used; forcing CRUD capability routes to `api/capabilities/` creates
unnecessary churn for API consumers and is confusing when the intent is entity management.

## Options

1. **Restore `api/entities/` for `[CrudCapabilityHandler]` handlers.**
   `MapCapabilities()` again branches on the attribute: handlers carrying
   `[CrudCapabilityHandler]` register under `api/entities/`; all others under
   `api/capabilities/`. `[CrudCapabilityHandler]` is both a semantic marker and a
   route-prefix selector, as it was under ADR-0006.

2. Keep ADR-0009 as-is (all under `api/capabilities/`).
   Con: API consumers who relied on `api/entities/` for generated CRUD must migrate
   client URLs without receiving any routing benefit in return.

## Decision

Option 1.

### Changes to `EndpointRouteBuilderExtensions`

Restore the prefix branch removed by ADR-0009:

```csharp
// Restored (this ADR)
var isCrud    = descriptor.HandlerType.GetCustomAttribute<CrudCapabilityHandlerAttribute>() is not null;
var prefix    = isCrud ? "api/entities" : "api/capabilities";
var routePath = $"{prefix}/{attr.Identity.ToRoutePath()}";
```

### Coexistence guarantee

`MapOperations()` registers collection-scoped REST routes (`api/entities/{path}`).
`MapCapabilities()` CRUD registers verb-suffixed routes (`api/entities/{path}/{verb}`).
The two sets are routable without ambiguity by ASP.NET Core. Applications that expose
both surfaces for the same entity type must verify that the path segments chosen for
each do not produce matching routes in their specific combination.

### Full changes table

| Action | File |
|--------|------|
| Create | `src/Capability.Http/adr/0010-restore-entities-prefix-for-crud-handlers.md` |
| Modify | `src/Capability.Http/EndpointRouteBuilderExtensions.cs` — restore dual-prefix logic |
| Modify | `tests/Capability.Http.Tests/MapCapabilitiesTests.cs` — restore `api/entities/` test cases |
| Modify | `samples/Application.Sample/bruno/02-books/**` — restore `api/entities/` URLs |
| Modify | `samples/Application.Sample/bruno/03-data-records/**` — restore `api/entities/` URLs |
| Modify | `samples/Application.Sample/bruno/07-entity-aspect-demo/**` — restore `api/entities/` URLs |
| Modify | `samples/Application.Sample/bruno/08-update-aspect-combined/**` — restore `api/entities/` URLs |
| Modify | `samples/Application.Sample/Program.cs` — update routing comment |

## Consequences

- `api/entities/{entity}/{verb}` is the route shape for `[CrudCapabilityHandler]` endpoints.
- `api/entities/{entity}` (REST) is the route shape for `[OperationEndpoints]` endpoints.
- API consumers of the generated CRUD capability surface do not need to migrate URLs.
- `[CrudCapabilityHandler]` remains both a semantic marker and a route-prefix selector.
- ADR-0009's migration of `ICapabilityAspectIriProvider` → `IExecutionAspectIriProvider`
  and the inline try/catch → `ExecutionEndpointHelper.InvokeAsync` refactoring remain in
  effect; only the prefix selection is reversed.
