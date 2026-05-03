# 0007 — Shapes provided dynamically per dispatch call; no generator

- **Status**: accepted; supersedes [0003](0003-message-attributes-drive-generation.md)
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0003 decided that a Roslyn source generator (`Forge.Capability.Generators`) would
read `[Command]`, `[Response]`, `[Event]` attributes on message classes and emit SHACL
TTL shapes as embedded resources plus startup registration code. The generator is not yet
implemented.

Before implementation begins, the requirements have changed: callers need to supply
shapes **per execution**, not globally at startup. A startup registry cannot express
"use shape X for this particular dispatch call but shape Y for the next". Concretely:

- The same command type may be invoked from multiple contexts that require different
  validation policies (tenant isolation, feature flags, staged rollout).
- Registering aspects once at application startup forces a single policy per type for
  the lifetime of the process.
- A generator tightly couples the validation policy to the message class definition,
  making it impossible to vary policy without changing the class.

## Options

1. **`CapabilityAspects` parameter on `DispatchAsync` — per-call injection.**
   The caller constructs a `CapabilityAspects` record (command/response/event aspects)
   and passes it to `DispatchAsync`. When `null`, all validation is permissive.
   The dispatcher requires no registry; the `IMessageAspectEngine` is its only
   aspect-infrastructure dependency. `[Command]`, `[Response]`, `[Event]` attributes
   and the generator project are eliminated.
2. **Keep the generator; add an optional per-call override.** Two code paths to maintain;
   complexity without clear benefit now that the per-call model covers all cases.
3. **Keep the registry; caller calls `registry.Register` at runtime.** Violates the
   registry's own contract (sealed after first read). Requires a mutable registry design,
   which introduces threading concerns.

## Decision

Option 1.

### New type

```csharp
public sealed record CapabilityAspects
{
    /// <summary>Aspect for the incoming command. Null = permissive.</summary>
    public IMessageAspect? CommandAspect { get; init; }

    /// <summary>Aspect for the outgoing response. Null = permissive.</summary>
    public IMessageAspect? ResponseAspect { get; init; }

    /// <summary>Aspects keyed by event CLR type. Missing key = permissive for that type.</summary>
    public IReadOnlyDictionary<Type, IMessageAspect> EventAspects { get; init; }
        = ImmutableDictionary<Type, IMessageAspect>.Empty;
}
```

### Updated dispatcher signature

```csharp
ValueTask<CapabilityResult<TResponse>> DispatchAsync(
    TCommand command,
    CapabilityAspects? aspects = null,
    CancellationToken cancellationToken = default);
```

- `aspects == null` → all three aspect slots are null → fully permissive execution.
- `CapabilityDispatcher` depends on `ICapabilityHandler` + `IMessageAspectEngine` only;
  `IMessageAspectRegistry` is no longer a constructor dependency.

### Eliminated artefacts

- `src/Capability/Attributes/CommandAttribute.cs`
- `src/Capability/Attributes/ResponseAttribute.cs`
- `src/Capability/Attributes/EventAttribute.cs`
- `Forge.Capability.Generators` project (not yet created; planned in ADR-0003)

### `CapabilityContext.EventAspects` contract (ADR-0002)

The handler still receives `CapabilityContext` with the resolved aspects. `EventAspects`
is populated from the caller-supplied `CapabilityAspects.EventAspects`. Because the caller
knows which event types the handler emits (it is the one designing the interaction), pre-
supplying a full `EventAspects` dictionary is feasible.

## Changes

| Action | File |
|--------|------|
| Create | `src/Capability/CapabilityAspects.cs` |
| Modify | `src/Capability/ICapabilityDispatcher.cs` |
| Modify | `src/Capability/CapabilityDispatcher.cs` |
| Modify | `src/Capability/DependencyInjection/CapabilityServiceCollectionExtensions.cs` |
| Delete | `src/Capability/Attributes/CommandAttribute.cs` |
| Delete | `src/Capability/Attributes/ResponseAttribute.cs` |
| Delete | `src/Capability/Attributes/EventAttribute.cs` |
| Adjust | ADR-0003 — marked superseded by 0007 |

## Consequences

- Callers supply aspects explicitly at dispatch time; no startup registration needed.
- The same dispatcher instance can be called with different aspects based on runtime context.
- If a project-wide default policy is desired, callers can build a `CapabilityAspects`
  factory from any source (configuration, registry, database) and pass it to `DispatchAsync`.
- The `IMessageAspectRegistry` and `[Command]`/`[Response]`/`[Event]` attributes are no
  longer part of the Capability dispatch contract. They remain available in `Forge.Aspects`
  for other consumers that need startup-time registration.
