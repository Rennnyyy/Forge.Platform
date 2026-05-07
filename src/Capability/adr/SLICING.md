# SLICING — Forge.Capability

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Capability` | Command/response dispatch contracts, execution context, result types, and the primary dispatcher implementation. | A file belongs here if it defines the primary Capability pattern: the `ICapabilityDispatcher<,>` and `ICapabilityHandler<,>` contracts, `CapabilityDispatcher<,>` implementation, `CapabilityContext`, `CapabilityAspects`, `CapabilityResult<>`, `CapabilityError`, `CapabilityIdentity`, and the `CapabilityAttribute` annotation. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Capability`)

- `ICapabilityDispatcher.cs` — typed command/response dispatcher contract.
- `ICapabilityHandler.cs` — application-code handler contract.
- `CapabilityDispatcher.cs` — default dispatcher implementation; resolves aspects, enforces authorization, and delegates to the handler.
- `CapabilityAttribute.cs` — marks a command class as a Capability entry point.
- `CapabilityContext.cs` — per-dispatch context carrying the resolved `CapabilityAspect`.
- `CapabilityAspects.cs` — value object grouping the command, response, and event aspects for a single dispatch.
- `CapabilityResult.cs` — discriminated union result carrying a value or an error.
- `CapabilityError.cs` — structured error returned when a dispatch fails.
- `CapabilityIdentity.cs` — identity information attached to the capability invocation.
