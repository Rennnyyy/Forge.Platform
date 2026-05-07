# SLICING — Forge.Authorization.Http

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Authorization.Http` | ASP.NET Core middleware for populating `AuthorizationContext`. | All HTTP-layer authorization types live here: `AgentTokenMiddleware` extracts the agent token from the incoming request and establishes the ambient `AuthorizationContext` scope. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Authorization.Http`)

- `AgentTokenMiddleware.cs` — ASP.NET Core middleware that reads the agent-token header and calls `AuthorizationContext.Use(token)` for the duration of the request.
