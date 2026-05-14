# 0019 — IAgentTokenAccessor: decouple CapabilityDispatcher from Forge.Authorization

- **Status**: accepted; supersedes [0008](0008-agent-token-in-capability-context.md)
- **Date**: 2026-05-11
- **Author**: agent

## Context

Capability ADR-0008 decided that `CapabilityDispatcher` reads
`AuthorizationContext.CurrentAgentToken` directly and forwards the value into
`CapabilityContext.AgentToken`. This required `Forge.Capability` to have a
`ProjectReference` to `Forge.Authorization`.

`Forge.Capability` is a core dispatch slice that should have no opinion about *how*
the agent identity is obtained — only that some accessor may supply it. The hard
dependency on `Forge.Authorization` means every consumer of `Forge.Capability` must
also transitively accept `Forge.Authorization`'s dependencies (repository guarding,
configuration, hosted services), even in contexts where authorization is irrelevant
(tests, lightweight CLR tools, async background processors).

## Decision

Introduce `IAgentTokenAccessor` in `Forge.Execution` (the shared, zero-dependency
execution-contracts slice):

```csharp
public interface IAgentTokenAccessor
{
    string? GetAgentToken();
}
```

`Forge.Authorization` provides `AuthorizationAgentTokenAccessor : IAgentTokenAccessor`
backed by `AuthorizationContext.CurrentAgentToken`. It is registered as a singleton by
`AddForgeAuthorization()` via `TryAddSingleton<IAgentTokenAccessor, AuthorizationAgentTokenAccessor>`.

`CapabilityDispatcher<TCommand, TResponse>` receives an optional
`IAgentTokenAccessor? tokenAccessor = null` constructor parameter (last, after `guard?`).
When non-null, it calls `tokenAccessor.GetAgentToken()` to capture the agent identity.
When null (e.g. in test scenarios that do not care about agent identity), the agent token
is `null`. The `ProjectReference` to `Forge.Authorization` is removed from
`Forge.Capability.csproj`.

## Consequences

- `Forge.Capability` depends only on `Forge.Aspects.Abstractions` and `Forge.Execution`.
  The `Forge.Authorization` dependency is gone.
- A host that calls `AddForgeAuthorization()` automatically has `IAgentTokenAccessor`
  registered; `CapabilityDispatcher` receives it through normal DI resolution.
- Hosts or tests that do not register `IAgentTokenAccessor` get a `null` agent token in
  `CapabilityContext` — the same behaviour as before for unauthenticated dispatch.
- Tests that verify agent token forwarding inject a mock `IAgentTokenAccessor` directly
  instead of relying on `AuthorizationContext.Use(...)`.
- `Forge.Capability.Tests` no longer references `Forge.Authorization`.
