# SLICING — Forge.Execution

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Execution` | Execution scope, correlation, result envelope, and structured error type. | All Execution types live here. This slice is intentionally flat: `ExecutionScope` manages the ambient `EntityOperations` lifetime, `ExecutionCorrelation` carries request-level tracing identifiers, `ExecutionResult` wraps a typed outcome, and `ExecutionError` is the structured error DTO used across HTTP responses. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Execution`)

- `ExecutionScope.cs` — ambient scope that owns the current `EntityOperations` lifetime; entered via `EntityOperations.Use(store)`.
- `ExecutionCorrelation.cs` — per-request correlation identifiers (trace ID, request ID).
- `ExecutionResult.cs` — generic discriminated-union result type (value or error).
- `ExecutionError.cs` — structured error DTO carrying a machine-readable code and human-readable message.
