# SLICING — Forge.Operations

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Operations` | Ambient entity operations scope and HTTP endpoint annotations. | All Operations types live here. This slice is intentionally flat: `EntityOperations` provides the ambient-scope facade over the current `IEntityStore`, `OperationEndpointsAttribute` annotates entity classes for automatic CRUD endpoint generation, and `NoOperationsAttribute` opts a class out of that generation. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Operations`)

- `EntityOperations.cs` — ambient-scope wrapper providing `ReadAsync`, `SaveAsync`, `DeleteAsync`, `ListAsync`, `BeginTransaction`, and `Use(store)` entry-point; delegates to the current scoped `IEntityStore`.
- `OperationEndpointsAttribute.cs` — marks an entity class as eligible for automatic CRUD REST endpoint generation by `Forge.Operations.Http`.
- `NoOperationsAttribute.cs` — suppresses CRUD endpoint generation for a specific entity class.
