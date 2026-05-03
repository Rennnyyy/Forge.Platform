# 0002 — `CapabilityContext` carries resolved aspects to the handler

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

A capability handler needs to know which validation policy is in effect for each message
it processes. The dispatcher resolves `IMessageAspect` instances from `IMessageAspectRegistry`
before invocation. How should this information reach the handler?

Three pressures:
1. Handlers should be aspect-aware for auditing, conditional branching, and logging.
2. Handlers should not query the registry directly — that couples business logic to
   infrastructure.
3. The contract should make aspect resolution explicit: a resolved null aspect means
   "no policy registered" not "a bug".

## Options

1. **Pass a `CapabilityContext` value to `HandleAsync`.** Resolved aspects are bundled
   into an immutable record the dispatcher constructs and injects. The handler sees exactly
   what aspects are active. No registry access inside handlers. Analogous to how
   `ValidationContext` propagates the agent token (`AsyncLocal`) but explicit rather than ambient.
2. **Ambient `AsyncLocal<CapabilityContext>`.** No signature change. Con: invisible at the
   call site; harder to test; harder to reason about in async flows.
3. **Handler does not receive aspect information.** Simpler signature. Con: handlers cannot
   branch on policy; auditing requires interceptors; no in-handler policy inspection possible.

## Decision

Option 1 — explicit `CapabilityContext` parameter.

### Contract

```csharp
public sealed class CapabilityContext
{
    /// <summary>Resolved aspect for the incoming command. Null = none registered → permissive.</summary>
    public IMessageAspect? CommandAspect { get; init; }

    /// <summary>Resolved aspect for the outgoing response. Null = permissive.</summary>
    public IMessageAspect? ResponseAspect { get; init; }

    /// <summary>Resolved aspects keyed by CLR event type. Missing key = permissive for that type.</summary>
    public IReadOnlyDictionary<Type, IMessageAspect> EventAspects { get; init; }
        = ImmutableDictionary<Type, IMessageAspect>.Empty;
}

public interface ICapabilityHandler<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    ValueTask<CapabilityResult<TResponse>> HandleAsync(
        TCommand command,
        CapabilityContext context,
        CancellationToken cancellationToken = default);
}

public sealed record CapabilityResult<TResponse> where TResponse : class
{
    public required TResponse Response { get; init; }
    public IReadOnlyList<object> Events { get; init; } = [];
}
```

### Dispatcher pipeline

```
① Resolve IMessageAspect for TCommand   → null = permissive
② IMessageAspectEngine.ValidateAsync(command, commandAspect)
③ Build CapabilityContext
④ handler.HandleAsync(command, context, ct)
⑤ Validate CapabilityResult.Response against ResponseAspect
⑥ For each event: resolve + validate IMessageAspect by event.GetType()
```

## Consequences

- Handler signatures are unambiguous: every aspect that applies is visible in the
  `CapabilityContext` argument.
- Tests can construct `CapabilityContext` directly without infrastructure; no registry mocks needed.
- The dispatcher is the single resolution point; handlers never call `IMessageAspectRegistry`.
