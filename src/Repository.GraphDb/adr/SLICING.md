# SLICING — Forge.Repository.GraphDb

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Repository.GraphDb` | GraphDB-backed implementations of the repository store contracts. | All GraphDB adapter types live here: `GraphDbEntityStore`, `GraphDbTransactionalStore`, `GraphDbSparqlQueryStore`, and `GraphDbOptions` for connection configuration. A file belongs here if it provides a concrete store implementation backed by a GraphDB HTTP endpoint. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Repository.GraphDb`)

- `GraphDbOptions.cs` — configuration options (endpoint URL, repository name, credentials).
- `GraphDbEntityStore.cs` — `IEntityStore` implementation backed by GraphDB via SPARQL 1.1 Update and Query.
- `GraphDbTransactionalStore.cs` — `ITransactionalEntityStore` implementation using GraphDB's transaction endpoint.
- `GraphDbSparqlQueryStore.cs` — `ISparqlQueryStore` implementation executing SPARQL SELECT/CONSTRUCT queries against GraphDB.
