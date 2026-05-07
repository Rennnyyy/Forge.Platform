# SLICING — Forge.Repository.InMemory

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Repository.InMemory` | In-memory implementations of the repository store contracts, used in unit tests and local development. | All in-memory adapter types live here: `InMemoryEntityStore`, `InMemoryTransactionalStore`, and `InMemorySparqlQueryStore`. A file belongs here if it provides a concrete store implementation backed by an in-process data structure. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Repository.InMemory`)

- `InMemoryEntityStore.cs` — `IEntityStore` implementation backed by a thread-safe in-process dictionary.
- `InMemoryTransactionalStore.cs` — `ITransactionalEntityStore` implementation with snapshot-based rollback semantics.
- `InMemorySparqlQueryStore.cs` — `ISparqlQueryStore` implementation using dotNetRDF's in-memory triple store for SPARQL execution.
