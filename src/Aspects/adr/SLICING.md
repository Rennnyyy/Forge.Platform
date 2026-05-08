# SLICING — Forge.Aspects

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Aspects` | Cross-cutting violation types and store decorators. | A file belongs here if it is a cross-cutting concern used by more than one sub-package: violation records/exceptions, aspect-TTL parse errors, and the store decorators that apply aspects on reads and writes. Contracts (`IAspect`, `IOperationAspect`, `IQueryAspect`, `IMessageAspect`, `MessageKind`, `IAspectStore`) live in `Forge.Aspects.Abstractions` to break circular dependencies. |
| `Message/` | `Forge.Aspects.Message` | Aspects that validate structured message objects (Capability payloads). | A file belongs here if its primary subject is a message aspect engine or registry: `IMessageAspectEngine`, `IMessageAspectRegistry`, their implementations, built-in message-aspect implementations, and message-specific violation types. The `IMessageAspect` contract and `MessageKind` live in `Forge.Aspects.Abstractions`. |
| `Query/` | `Forge.Aspects.Query` | Aspects that apply to read/query operations: filter injection + result-graph SHACL. | A file belongs here if its primary subject is a query-aspect engine, scope, built-in implementation, or query-specific violation type. The `IQueryAspect` contract lives in `Forge.Aspects.Abstractions`. |
| `Operation/` | `Forge.Aspects.Operation` | Aspects that validate write operations (create, update, delete) via SHACL + SPARQL, plus the shape cache used by the engine. | A file belongs here if its primary subject is a write-operation aspect engine, built-in implementation, or the shape cache infrastructure consumed by the engine. The `IOperationAspect` contract lives in `Forge.Aspects.Abstractions`. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Aspects`)

- `AspectStore.cs` — default `IAspectStore` implementation (stores registered aspects).
- `AspectViolation.cs` — record shared by all violation exceptions.
- `AspectViolationException.cs` — base violation exception (write path).
- `AspectTtlParseException.cs` — thrown when a shape TTL string cannot be parsed.
- `AspectEnforcingEntityStore.cs` — decorator that applies query aspects on reads.
- `AspectEnforcingTransactionalStore.cs` — decorator that applies operation aspects on writes.

### `Message/` (`Forge.Aspects.Message`)

- `IMessageAspectEngine.cs`
- `IMessageAspectRegistry.cs`
- `MessageAspectEngine.cs`
- `MessageAspectRegistry.cs`
- `MessageAspectViolationException.cs`
- `InlineTtlMessageAspect.cs`

### `Query/` (`Forge.Aspects.Query`)

- `IQueryAspectEngine.cs`
- `QueryAspectEngine.cs`
- `QueryAspectScope.cs`
- `QueryAspectViolationException.cs`
- `InlineTtlQueryAspect.cs`

### `Operation/` (`Forge.Aspects.Operation`)

- `IOperationAspectEngine.cs`
- `OperationAspectEngine.cs`
- `InlineTtlOperationAspect.cs`
- `IShapeCache.cs`
- `ShapeCache.cs`

