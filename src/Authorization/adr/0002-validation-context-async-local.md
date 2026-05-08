# 0002 — `ValidationContext`: ambient agent-token binding via AsyncLocal

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

`IOperationGuard` requires an `agentToken` on every call. In a web application the token
originates from an incoming HTTP request (e.g. a `Bearer` JWT). It must flow through the
async call stack to the `GuardedTransactionalStore` without every intermediate call site
needing to forward it explicitly.

The platform already uses this pattern: `EntityOptions.Use(options)` and
`EntityOperations.Use(store)` both bind a value to an `AsyncLocal<T>` for the current
async control flow. The same mechanism works here.

## Options

1. **`ValidationContext.Use(agentToken)` — `AsyncLocal<string?>` binding; current token
   is `ValidationContext.CurrentAgentToken`.** Returns `IDisposable` scope. Consistent
   with existing platform ambient binding pattern.
2. **Pass `agentToken` explicitly through `EntityTransaction` / generated methods.**
   Con: every call site changes; generated code must carry a new parameter; cross-cutting
   concern leaks into every layer.
3. **`IHttpContextAccessor` integration only.** Con: ties the core library to the ASP.NET
   hosting model; breaks non-web consumers.

## Decision

Option 1.

- `ValidationContext.Use(agentToken)` — binds the token for the current async context.
  Returns an `IDisposable` that restores the previous value on dispose.
- `ValidationContext.CurrentAgentToken` — the currently bound token, or `null`.
- `GuardedTransactionalStore` uses `ValidationContext.CurrentAgentToken ?? string.Empty`
  so that tests using `AllowAllOperationGuard` (which ignores the token) do not require a
  `Use(…)` boilerplate scope. Stricter guards should treat an empty agent token as
  anonymous / unauthenticated and reject it if needed.

## Consequences

- Host code (e.g. ASP.NET Core middleware) calls `ValidationContext.Use(jwt)` at the
  request boundary; all downstream operations in that request carry the token automatically.
- Tests using `AllowAllOperationGuard` need no `ValidationContext.Use(…)` call.
- A stricter guard that rejects empty tokens forces all callers to set a context first,
  which is the desired behavior for authenticated scenarios.

> *`ValidationContext` renamed to `AuthorizationContext` due to a later refactor aligning on the Authorization namespace. All references to `ValidationContext.Use` and `ValidationContext.CurrentAgentToken` in this ADR should be read as `AuthorizationContext.Use` and `AuthorizationContext.CurrentAgentToken`.*
