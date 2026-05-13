# SLICING — Forge.ObjectStorage.Abstractions

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.ObjectStorage` | Provider-agnostic blob-store interfaces. | A file belongs here if it defines a contract or record shared across all object-storage implementations: `IObjectStore`, `IObjectStoreProvider`. No blob-SDK references are permitted. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.ObjectStorage`)

- `IObjectStore.cs` — 4-method async blob interface: `UploadAsync`, `DownloadAsync`, `DeleteAsync`, `ExistsAsync`.
- `IObjectStoreProvider.cs` — resolves an `IObjectStore` by DI key string; allows applications to register multiple named stores.
