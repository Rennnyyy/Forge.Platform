# 0009 — Rename `ICapabilityAspectGuard` → `IAspectGuard`; drop `MessageKind` parameter

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0009 introduced `ICapabilityAspectGuard` with signature:

```csharp
ValueTask AuthorizeAsync(string agentToken, string aspectToken, MessageKind kind, CancellationToken ct)
```

Two problems were identified:

1. **`MessageKind` is redundant.** The aspect token (`commandAspect?.Name`) already encodes the
   policy identity. The guard implementation can distinguish message roles via aspect naming
   conventions, not via a CLR enum. Carrying `MessageKind` provides no information that a
   well-named aspect token doesn't already carry.
2. **`MessageKind` creates an unnecessary `Forge.Aspects.Message` dependency** on the
   interface itself. `IOperationGuard` in `Forge.Validation` uses only plain strings and
   remains dependency-free. `IAspectGuard` should follow the same discipline so that a
   future move to `Forge.Validation` is not blocked by a transitive dependency.
3. **`ICapabilityAspectGuard` is an overly qualified name.** The interface is already inside
   `Forge.Capability`; the `Capability` infix is redundant. `IAspectGuard` is shorter and
   consistent with `IOperationGuard` naming in `Forge.Validation`.

## Decision

- Rename `ICapabilityAspectGuard` → `IAspectGuard`.
- Rename `AllowAllCapabilityAspectGuard` → `AllowAllAspectGuard`.
- Remove the `MessageKind kind` parameter from `AuthorizeAsync`.

### New interface contract

```csharp
// src/Capability/IAspectGuard.cs
public interface IAspectGuard
{
    ValueTask AuthorizeAsync(
        string agentToken,
        string aspectToken,
        CancellationToken cancellationToken = default);
}
```

### Dispatcher pipeline (updated step ①–⑥ from ADR-0009)

Guard calls become:
```csharp
await _guard.AuthorizeAsync(agentToken, commandAspect?.Name  ?? Aspect.NoOp.Name, ct);  // ②
await _guard.AuthorizeAsync(agentToken, responseAspect?.Name ?? Aspect.NoOp.Name, ct);  // ⑥a
await _guard.AuthorizeAsync(agentToken, eventAspect?.Name    ?? Aspect.NoOp.Name, ct);  // ⑥b
```

## Changes

| Action | File |
|--------|------|
| Create | `src/Capability/IAspectGuard.cs` |
| Create | `src/Capability/AllowAllAspectGuard.cs` |
| Delete | `src/Capability/ICapabilityAspectGuard.cs` |
| Delete | `src/Capability/AllowAllCapabilityAspectGuard.cs` |
| Modify | `src/Capability/CapabilityDispatcher.cs` — rename + drop `MessageKind` arg |
| Modify | `src/Capability/DependencyInjection/CapabilityServiceCollectionExtensions.cs` — rename |
| Modify | `tests/Capability.Tests/CapabilityDispatcherTests.cs` — rename + drop `MessageKind` arg |
| Adjust | ADR-0009 — marked superseded by 0010 |

## Consequences

- `IAspectGuard` has no dependency on `Forge.Aspects.Message`, making a future move to
  `Forge.Validation` straightforward.
- Guard implementations receive exactly the same two tokens as `IOperationGuard.AuthorizeQueryAsync`
  — a uniform authorization vocabulary across all enforcement points.
- Callers that need to distinguish message roles encode that in the aspect name, not the
  enum value.
