# SLICING — Forge.Operations.Http

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Operations.Http` | ASP.NET Core endpoint registration, request/response binding, and response envelope types for `[OperationEndpoints]`-annotated entities. | A file belongs here if it bridges the Operations pattern to the HTTP layer: reading entity classes annotated with `[OperationEndpoints]`, generating Minimal API routes for list/read/save/delete, binding incoming JSON to entity instances, and formatting the response envelopes. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Operations.Http`)

- `OperationEndpointsEndpointRouteBuilderExtensions.cs` — `MapOperationEndpoints` extension that reflects over registered entity types and registers GET/PUT/DELETE Minimal API routes for each `[OperationEndpoints]` class.
- `OperationEndpointDescriptor.cs` — describes a discovered operation endpoint (route, entity type, HTTP verb).
- `OperationEntityBinder.cs` — deserializes the incoming JSON request body to the target entity type using the configured `IRdfMapper`.
- `OperationResponses.cs` — typed response envelope records returned by the generated endpoints.
- `EntityOwningRelationConverters.cs` — JSON converters for owning-relation collections within entity payloads.
