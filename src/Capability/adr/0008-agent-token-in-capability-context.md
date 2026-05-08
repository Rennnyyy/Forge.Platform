# 0008 — Dispatcher reads ambient agent token into `CapabilityContext`

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

`Forge.Authorization` already provides `AuthorizationContext.CurrentAgentToken` — an ambient
`AsyncLocal<string?>` binding established by host code (e.g. ASP.NET Core middleware) via
`ValidationContext.Use(agentToken)`. The `GuardedTransactionalStore` reads this token
automatically on every `ExecuteTransactionAsync`, `LoadAsync`, and `QueryByTypeAsync`.

A capability handler that performs store operations already benefits from this ambient
binding — the `GuardedTransactionalStore` sees the correct agent identity without explicit
forwarding. However, the handler itself has no convenient access to **which** agent is
executing the dispatch: it would have to call `ValidationContext.CurrentAgentToken`
directly, coupling handler business logic to the Validation infrastructure type.

Two related clarifications on terms used elsewhere:

- **Aspect token** — in the existing codebase this is `IAspect.Name`, a string name
  carried on each `TransactionOperation`. At the dispatcher level the "aspect" is the
  `IMessageAspect` instance already present in `CapabilityContext.CommandAspect`,
  `ResponseAspect`, and `EventAspects`. Handlers that need the policy name can read
  `context.CommandAspect?.Name` directly. No separate `AspectToken` string property is
  needed on `CapabilityContext`.
- **Agent token in `CapabilityAspects`** — the agent token cannot be supplied via
  `CapabilityAspects` because it is ambient: it belongs to the call stack, not to a
  per-dispatch message. The dispatcher is the right place to capture and forward it.

## Options

1. **Dispatcher reads `ValidationContext.CurrentAgentToken` and populates
   `CapabilityContext.AgentToken`.** The handler sees the ambient identity through the
   context parameter it already receives, with no direct dependency on `Forge.Validation`.
   `Forge.Capability` gains a `<ProjectReference>` to `Forge.Validation` (already on the
   same hosting stack as any realistic consumer).
2. **Handler calls `ValidationContext.CurrentAgentToken` directly.**
   Con: couples every handler to `Forge.Validation`; leaks infrastructure into business code.
3. **Ignore agent token in the dispatcher; expose it only through `GuardedTransactionalStore`.**
   Con: handler cannot log, audit, or branch on the caller identity without calling back
   into the ambient context.

## Decision

Option 1.

### Change to `CapabilityContext`

```csharp
/// <summary>
/// The agent identity token from <see cref="ValidationContext.CurrentAgentToken"/>
/// at the moment <see cref="ICapabilityDispatcher{TCommand,TResponse}.DispatchAsync"/>
/// was called. Null if no <c>ValidationContext.Use(…)</c> scope was active.
/// </summary>
public string? AgentToken { get; init; }
```

### Dispatcher pipeline (updated)

```
① Capture agentToken = ValidationContext.CurrentAgentToken (null if no scope active)
② engine.ValidateAsync(command, commandAspect)
③ Build CapabilityContext { ..., AgentToken = agentToken }
④ handler.HandleAsync(command, context, ct)
⑤ if Ok: engine.ValidateAsync(response, responseAspect)
   for each event: engine.ValidateAsync(evt, eventAspect)
⑥ return result
```

No scope is opened or closed by the dispatcher. The dispatcher reads the ambient state
established upstream by the host; it does not manage the lifetime of that scope.

## Changes

| Action | File |
|--------|------|
| Modify | `src/Capability/CapabilityContext.cs` — add `AgentToken` property |
| Modify | `src/Capability/CapabilityDispatcher.cs` — capture and forward agent token |
| Modify | `src/Capability/Forge.Capability.csproj` — add `<ProjectReference>` to `Forge.Validation` |
| Modify | `tests/Capability.Tests/CapabilityDispatcherTests.cs` — add agent token tests |
| Modify | `src/Capability/adr/README.md` — index entry |

## Consequences

- Handlers read the dispatching agent's identity from `context.AgentToken` without
  depending on `Forge.Validation` or `AsyncLocal` internals.
- `Forge.Capability` gains a dependency on `Forge.Validation`. Both are in-process
  libraries on the same hosting stack; no circular dependency is introduced.
- The dispatcher does not own the agent token scope — it only observes and forwards it.
  Host code remains the sole owner of `ValidationContext.Use(…)` lifetimes.
- Tests that do not establish a `ValidationContext` scope will see `context.AgentToken == null`,
  which is the correct and expected behavior.

> *`ValidationContext` renamed to `AuthorizationContext` due to a later refactor aligning on the Authorization namespace. All references to `ValidationContext.Use` and `ValidationContext.CurrentAgentToken` in this ADR should be read as `AuthorizationContext.Use` and `AuthorizationContext.CurrentAgentToken`.*
