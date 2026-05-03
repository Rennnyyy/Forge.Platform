# 0001 — `IMessageAspect` as the third aspect leg

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

`Forge.Aspects` already defines two validation legs keyed by `IOperationAspect`:

| Leg | Interface | Domain |
|---|---|---|
| Write | `IWriteAspect : IOperationAspect` | CUD operations — Local SHACL + Context SPARQL |
| Read | `IQueryAspect : IOperationAspect` | Queries — filter gate + result-graph SHACL |

Capability messages (commands, responses, events) are boundary objects that carry
structured data into and out of a handler. They require the same class of structural contract
enforcement — shape validation — but are not `TransactionOperation`s or SPARQL query
results. A third validation leg is needed.

The key design constraint: if no shape is registered for a message type, dispatch is
**permissive** — `null` is the explicit "no policy" state, not an error. This inverts
the `IAspectResolver.Resolve` contract (which throws on miss) because unvalidated messages
are intentional, not a misconfiguration.

## Options

1. **`IMessageAspect : IOperationAspect` in `Forge.Aspects`; null-on-miss registry.**
   Follows the established leg naming (`I<Domain>Aspect`). Lives in `Forge.Aspects`
   alongside the other two legs — no new project dependency. Registry returns `null`
   (not `Aspect.NoOp`) on miss; engine silently skips. Shape data is SHACL-only:
   no SPARQL Context pass (messages are ephemeral; there is no graph store to query against).
2. **Reuse `IWriteAspect` with a message-projection adapter.** Less surface area, but
   conflates CUD-operation concerns (entity IRI, operation kind) with message concerns.
   `IWriteAspect.ContextWhere` would be meaningless for messages — vestigial properties
   on every message aspect.
3. **Separate `Forge.Message` project for `IMessageAspect`.** Con: an extra project for
   a two-property interface; `Forge.Aspects` already depends on nothing that would
   cause a cycle.

## Decision

Option 1.

### Contract

```csharp
// Forge.Aspects/IMessageAspect.cs
public interface IMessageAspect : IOperationAspect
{
    /// <summary>
    /// Turtle-serialized SHACL shape validated against the message graph,
    /// or null if no shape check is required.
    /// </summary>
    string? ShapeTtl { get; }
}
```

```csharp
// Forge.Aspects/MessageKind.cs
[Flags]
public enum MessageKind
{
    Command  = 1,
    Response = 2,
    Event    = 4,
}
```

Registry contract (null-on-miss, no throw):

```csharp
public interface IMessageAspectRegistry
{
    IMessageAspect? TryGet(Type messageType, MessageKind kind);
    void Register(IMessageAspect aspect, Type messageType, MessageKind kind);
}
```

Engine:

```csharp
public interface IMessageAspectEngine
{
    /// <summary>Validate message against aspect. No-op if aspect is null or ShapeTtl is null.</summary>
    ValueTask ValidateAsync(object message, IMessageAspect? aspect, CancellationToken ct = default);
}
```

## Consequences

- The three legs now share a consistent naming pattern: `IWriteAspect`, `IQueryAspect`, `IMessageAspect`.
- Permissive default (null) is structurally distinct from fail-fast default (`AspectNotRegisteredException`)
  used by the write leg; both are intentional and documented.
- `Forge.Capability` references `Forge.Aspects` and uses `IMessageAspectEngine` + `IMessageAspectRegistry`
  injected at the dispatcher layer.

> *`IOperationAspect` (base token) renamed to `IAspect` due to Aspects ADR-0009. `IWriteAspect` renamed to `IOperationAspect` simultaneously. `IMessageAspect : IAspect`. The naming pattern is now: `IOperationAspect`, `IQueryAspect`, `IMessageAspect`.*
