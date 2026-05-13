# SLICING — Forge.ObjectStorage.Http

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.ObjectStorage.Http` | ASP.NET Core endpoint registration for `[ObjectBearing]`-annotated entities: metadata CRUD + binary upload/download channel. | A file belongs here if it bridges blob-owning entities to the HTTP layer: route registration, upload saga orchestration, download streaming, and aspect-IRI threading. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.ObjectStorage.Http`)

- `ObjectOperationsEndpointRouteBuilderExtensions.cs` — `MapObjectOperations()` extension that discovers `[ObjectBearing]` entity types and registers the 5-route set per type.
- `ObjectOperationDescriptor.cs` — describes a discovered object-bearing endpoint (entity type, route path, store key).
- `ObjectUploadSaga.cs` — orchestrates the convention-key staging protocol: stage upload → entity update → promote → cleanup.
