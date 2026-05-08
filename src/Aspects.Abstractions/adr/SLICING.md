# SLICING — Forge.Aspects.Abstractions

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Aspects.Abstractions` | Pure aspect contracts and well-known constants. | All files live at the root. No sub-folders: the assembly has no frameworks, no engines, and no external dependencies — only BCL types. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Aspects.Abstractions`)

- `IAspect.cs` — marker interface for a named validation policy (e.g. `"book-pub-year-v1"`).
- `Aspect.cs` — well-known aspect token singletons (`NoOp`).
- `IAspectStore.cs` — contract for resolving a registered `IOperationAspect` by IRI.
- `IOperationAspect.cs` — contract for a SHACL + SPARQL write-path validation policy.
- `IQueryAspect.cs` — contract for a read-path filter-injection + result-graph validation policy.
- `IMessageAspect.cs` — contract for a capability-message validation policy.
- `MessageKind.cs` — enum distinguishing `Command` from `Response` in message aspects.
- `CapabilityAspect.cs` — well-known capability aspect token (e.g. `CapabilityAspect.Message`).
- `AspectNotFoundException.cs` — thrown when no `IOperationAspect` is registered for a given IRI.
