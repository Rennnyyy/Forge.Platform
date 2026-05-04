# 0011 — Assembly-scanning registration: `AddCapabilityHandlersFromAssemblyContaining<T>`

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0006 established `AddCapabilityHandler<TCommand, TResponse, THandler>()` as the DI
registration method for a capability handler pair. In applications with many handlers, one
explicit call per handler is repetitive and error-prone — a handler can easily be omitted,
causing a silent gap in the capability surface.

A convention-based scanner that discovers all `ICapabilityHandler<,>` implementations in
an assembly eliminates the per-handler boilerplate while keeping the existing per-handler
method for cases that need precise control (e.g. handlers that must be registered after
`AddCapabilityHttp()` to opt out of auto-route mapping — see Capability.Http ADR-0004).

## Options

1. **`AddCapabilityHandlersFromAssemblyContaining<TMarker>(IServiceCollection)`** —
   scans the assembly containing `TMarker` for non-abstract classes that implement
   `ICapabilityHandler<TCommand, TResponse>` and calls
   `AddCapabilityHandler<TCommand, TResponse, THandler>` for each. Uses reflection
   internally; the public API is generic and type-safe at the marker level.
2. **`AddCapabilityHandlersFromAssembly(Assembly)`** — accepts the assembly directly.
   Pro: explicit. Con: `typeof(MyHandler).Assembly` is more verbose than a marker type;
   callers outside the assembly cannot easily pinpoint a marker type.
3. **Source-generator approach.** Con: significant complexity; the existing generator
   budget is allocated to `Forge.Operations`; no current requirement.

## Decision

Option 1.

### Contract

```csharp
public static IServiceCollection AddCapabilityHandlersFromAssemblyContaining<TMarker>(
    this IServiceCollection services);
```

### Behaviour

- Iterates `typeof(TMarker).Assembly.GetTypes()`.
- For each non-abstract class implementing one or more closed forms of
  `ICapabilityHandler<TCommand, TResponse>`, calls
  `AddCapabilityHandler<TCommand, TResponse, THandler>(services)` once per
  `ICapabilityHandler<,>` interface found on the type.
- Uses `TryAddTransient` internally (inherited from `AddCapabilityHandler`) so
  calling the scanner and an explicit `AddCapabilityHandler` for the same type is safe
  (the second call is a no-op).

### Ordering constraint

The ordering rules of `AddCapabilityHandler` apply unchanged:
`AddCapabilityHandlersFromAssemblyContaining<T>` must be called **before**
`AddCapabilityHttp()` for auto-route-mapped handlers. Handlers that must be excluded
from auto-route mapping (e.g. GET handlers requiring manual route registration — see
Capability.Http ADR-0004) must be registered explicitly after `AddCapabilityHttp()`.

## Consequences

- Applications with many handlers need only one scanner call.
- The existing per-handler method remains the escape hatch for fine-grained control.
- The scanner uses reflection at startup time (once); no runtime overhead.
