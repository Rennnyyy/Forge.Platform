# 0001 — Authorization.Http slice: agent-token middleware

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

`Forge.Authorization` defines `AuthorizationContext.Use(agentToken)` — an `AsyncLocal`
ambient scope that propagates the calling agent's identity to `GuardedTransactionalStore`
and `CapabilityDispatcher`. The token must be established at the top of the HTTP request
pipeline so that every downstream operation within the same request observes the correct
identity.

ASP.NET Core middleware is the canonical place to establish per-request ambient state.
Without a dedicated slice for this concern, every HTTP application would need to duplicate
the token-extraction logic.

## Options

1. **`Forge.Authorization.Http`** — a thin slice with a single `AgentTokenMiddleware` that
   reads `Authorization: Bearer <token>` from the request header and calls
   `AuthorizationContext.Use(token)` before invoking the next middleware.
   An `IApplicationBuilder.UseAgentTokenMiddleware()` extension wires it.
   No other concerns belong in this slice.
2. Bake the middleware directly into `Forge.Capability.Http`.
   Con: couples agent-token extraction to the capability routing concern; applications
   that want agent-token propagation without capability HTTP integration cannot use it
   independently.
3. Bake the middleware into `Forge.Authorization`.
   Con: forces an ASP.NET Core runtime dependency onto every consumer of
   `Forge.Authorization`, including pure domain code.

## Decision

Option 1.

### Dependency graph

```
Forge.Authorization.Http
  → Forge.Authorization
  → Microsoft.AspNetCore.App (FrameworkReference)
```

### Middleware behaviour

- Header present and value is `Bearer <token>` (case-insensitive prefix match, non-empty
  token after trimming): establishes `AuthorizationContext.Use(token)` around `next(context)`.
- All other cases (absent header, unsupported scheme, empty token): calls `next(context)`
  with no scope established; `AuthorizationContext.CurrentAgentToken` remains `null`.
- The middleware does not reject unauthenticated requests — that is the guard's concern.

## Consequences

- `Forge.Capability.Http` can depend on `Forge.Authorization.Http` for agent-token
  propagation without pulling in the middleware itself.
- `Forge.Authorization` remains usable in non-HTTP contexts (pure in-process, tests,
  future messaging).
- Applications chain: `app.UseAgentTokenMiddleware()` before `app.MapCapabilities()`.
