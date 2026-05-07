# SLICING — Forge.Authorization

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Authorization` | Authorization contracts, guard implementations, and store decorator. | All public authorization types live here: the `IAspectGuard` contract, the `AllowAllAspectGuard` default, `AuthorizationContext` for ambient agent-token propagation, and `GuardedTransactionalStore` which enforces authorization on write operations. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Authorization`)

- `IAspectGuard.cs` — contract for authorizing a given aspect IRI on behalf of an agent.
- `AllowAllAspectGuard.cs` — permissive no-op guard for development and testing scenarios.
- `AuthorizationContext.cs` — ambient `AsyncLocal` scope carrying the current agent token.
- `GuardedTransactionalStore.cs` — transactional-store decorator that calls `IAspectGuard` before each write operation.
