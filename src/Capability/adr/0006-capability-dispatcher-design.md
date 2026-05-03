# 0006 — `ICapabilityDispatcher<TCommand,TResponse>`: dispatcher pipeline implementation

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0002 defines the six-step dispatcher pipeline (resolve aspects → validate command →
build context → call handler → validate response → validate events) but does not name
the implementing type or specify its DI registration pattern. Without a named interface
callers cannot resolve the dispatcher from DI, and there is no canonical place to test
the full orchestration.

Three design questions arise:

1. **Interface or concrete type?** Should callers depend on an interface (testable) or
   directly on the `CapabilityDispatcher<T,R>` class?
2. **DI registration style.** Should the dispatcher be registered per handler pair via a
   helper extension, or via a global open-generic registration?
3. **`EventAspects` in `CapabilityContext`.** The dispatcher builds the context _before_
   calling the handler, but event types are not known until after the handler returns.
   How should `CapabilityContext.EventAspects` be populated?

## Options

### Interface design

1. **`ICapabilityDispatcher<TCommand, TResponse>` with a single `DispatchAsync`** —
   small, testable, mirrors `ICapabilityHandler`. Handler is a constructor dependency.
2. **No interface; concrete only.** Pro: less surface area. Con: callers cannot mock the
   dispatcher; no DI abstraction boundary.

### `EventAspects` population

1. **Always `ImmutableDictionary.Empty`** — event types are unknown pre-execution.
   Post-execution validation uses `IMessageAspectRegistry` directly.
   "Missing key = permissive" (ADR-0002) remains satisfied: an empty dictionary means
   all event types are permissive, which is correct until the handler emits events.
2. **Lazy dictionary backed by the registry** — a virtual `IDictionary` proxy that calls
   `TryGet` on access. Pro: handler can inspect event policy. Con: over-engineering for
   a use case with no current caller.

## Decision

**Interface option 1 / EventAspects option 1.**

### Type contract

```csharp
// src/Capability/ICapabilityDispatcher.cs
public interface ICapabilityDispatcher<TCommand, TResponse>
    where TCommand : class
    where TResponse : class
{
    ValueTask<CapabilityResult<TResponse>> DispatchAsync(
        TCommand command,
        CancellationToken cancellationToken = default);
}
```

### Pipeline (implements ADR-0002 §Dispatcher pipeline)

```
① registry.TryGet(typeof(TCommand), MessageKind.Command)   → commandAspect (null = permissive)
② engine.ValidateAsync(command, commandAspect)
③ registry.TryGet(typeof(TResponse), MessageKind.Response) → responseAspect (null = permissive)
④ Build CapabilityContext { CommandAspect, ResponseAspect, EventAspects = ImmutableDictionary.Empty }
⑤ handler.HandleAsync(command, context, ct) → result
⑥ if result is Ok: engine.ValidateAsync(response, responseAspect)
   for each event in result.Events: registry.TryGet(evt.GetType(), MessageKind.Event) + engine.ValidateAsync
⑦ Return result unchanged
```

`EventAspects` is always `ImmutableDictionary.Empty` in the context passed to the handler.
Handlers that must inspect event policy for pre-flight branching may inject
`IMessageAspectRegistry` directly; this is expected to be rare.

### DI registration

```csharp
services.AddCapabilityHandler<TCommand, TResponse, THandler>()
```

Registers `ICapabilityHandler<TCommand, TResponse>` as transient `THandler` and
`ICapabilityDispatcher<TCommand, TResponse>` as transient `CapabilityDispatcher<TCommand, TResponse>`.
Requires `IMessageAspectRegistry` and `IMessageAspectEngine` to already be in the container
(i.e. `AddForgeAspects()` must be called first).

## Consequences

- `ICapabilityDispatcher<TCommand, TResponse>` is the callable boundary; handlers are
  internal dispatcher details from the caller's perspective.
- `EventAspects` is always empty in the handler's context; event validation is a
  dispatcher-post-execution concern only.
- Tests exercise the full pipeline by mocking `IMessageAspectEngine` and
  `IMessageAspectRegistry`; no dotNetRDF dependency in `Forge.Capability.Tests`.
- Adding event-aspect inspection inside a handler requires explicit `IMessageAspectRegistry`
  injection — a deliberate friction that discourages handlers from bypassing the
  dispatcher's validation contract.
