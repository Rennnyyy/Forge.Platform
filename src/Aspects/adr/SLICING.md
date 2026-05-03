# SLICING — Forge.Aspects

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Aspects` | Core engine types, cross-cutting violations, store decorators. | A file belongs here if it is either the central orchestration engine (`AspectEngine`, `IAspectEngine`, `IAspectResolver`) or a cross-cutting type used by more than one of the sub-concerns (e.g. `AspectKind`, `AspectViolation`, `AspectViolationException`, error types, store decorators). |
| `Message/` | `Forge.Aspects.Message` | Aspects that validate structured message objects (Capability payloads). | A file belongs here if its primary subject is a message aspect: the `IMessageAspect` contract, its engine, registry, built-in implementations, and message-specific violation types. |
| `Query/` | `Forge.Aspects.Query` | Aspects that apply to read/query operations: filter injection + result-graph SHACL. | A file belongs here if its primary subject is a query aspect: the `IQueryAspect` contract, its engine, scope, built-in implementations, and query-specific violation types. |
| `Operation/` | `Forge.Aspects.Operation` | Aspects that validate write operations (create, update, delete) via SHACL + SPARQL. | A file belongs here if its primary subject is a write-operation aspect: the `IOperationAspect` contract and built-in implementations. |
| `Shape/` | `Forge.Aspects.Shape` | SHACL shape registry and TTL cache used by the engines. | A file belongs here if it deals with shape metadata storage or retrieval: `IShapeCache`, `IShapeRegistry`, and their implementations. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Aspects`)

- `AspectEngine.cs` — orchestrates Local + Context passes; references all sub-concerns.
- `IAspectEngine.cs` — public contract for the orchestrator.
- `IAspectResolver.cs` — resolves a declared aspect to a registered `IOperationAspect`.
- `AspectKind.cs` — enum shared across all aspect kinds.
- `AspectViolation.cs` — record shared by all violation exceptions.
- `AspectViolationException.cs` — base violation exception (write path).
- `AspectNotRegisteredException.cs` — thrown when no registration is found.
- `AspectTtlParseException.cs` — thrown when a shape TTL string cannot be parsed.
- `AspectEnforcingEntityStore.cs` — decorator that applies query aspects on reads.
- `AspectEnforcingTransactionalStore.cs` — decorator that applies operation aspects on writes.

### `Message/` (`Forge.Aspects.Message`)

- `IMessageAspect.cs`
- `IMessageAspectEngine.cs`
- `IMessageAspectRegistry.cs`
- `MessageAspectEngine.cs`
- `MessageAspectRegistry.cs`
- `MessageAspectViolationException.cs`
- `MessageKind.cs`
- `InlineTtlMessageAspect.cs`

### `Query/` (`Forge.Aspects.Query`)

- `IQueryAspect.cs`
- `IQueryAspectEngine.cs`
- `QueryAspectEngine.cs`
- `QueryAspectScope.cs`
- `QueryAspectViolationException.cs`
- `InlineTtlQueryAspect.cs`

### `Operation/` (`Forge.Aspects.Operation`)

- `IOperationAspect.cs`
- `InlineTtlWriteAspect.cs`

### `Shape/` (`Forge.Aspects.Shape`)

- `IShapeCache.cs`
- `IShapeRegistry.cs`
- `ShapeCache.cs`
- `ShapeRegistry.cs`
