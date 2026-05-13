# 0001 — Object-bearing HTTP routes — 5-verb resource model with binary channel

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

`MapOperations()` emits JSON-only CRUD for `[OperationEndpoints]` entity types (Operations.Http
ADR-0001). It explicitly skips any entity annotated with `[ObjectBearing]` (Entity ADR-0019).
A dedicated HTTP layer is needed that covers:

- JSON metadata CRUD — create, read, list, delete — using the shared response contracts
  (ADR-0017).
- Binary upload — a streaming `PUT` that stores bytes in `IObjectStore` and updates the
  entity's `ObjectKey`/`ContentType` via `EntityTransaction`.
- Binary download — a `GET` that streams bytes from `IObjectStore` and uses the entity
  load as the implicit aspect gate (per design decision: download is treated as a read;
  if loading the entity succeeds, downloading the bytes is permitted).

This layer is a managed-entity HTTP layer subject to all four ADR-0019 obligations:

| Obligation | Requirement |
|------------|-------------|
| 1. Shared response contracts | `OperationCreatedResponse`, `OperationUpdatedResponse`, `OperationDeletedResponse`, `OperationListResponse<T>` from `Forge.Operations.Http` on JSON endpoints. Binary download endpoints are exempt — they return a raw byte stream, not a JSON envelope. |
| 2. Aspect enforcement on backing store | `AddForgeAspectsForKeyedStore(storeKey, ...)` wraps the keyed `ITransactionalEntityStore`. |
| 3. Aspect IRI threading | `X-Forge-Operation-AspectIri` read via `HeaderExecutionAspectIriProvider` and passed to all CUD `EntityTransaction` operations. |
| 4. Managed-entity store key registration | `AddForgeObjectStorageHttp()` registers `ManagedEntityStoreKeyRegistration(storeKey)` in DI. |

## Options

### Option A — `MapObjectOperations()` is a separate extension on `IEndpointRouteBuilder`

Pro: same pattern as `MapOperations()` — one call wires everything; discoverable.
Con: must discover `[ObjectBearing]` types independently (cannot reuse `OperationEndpointDescriptor`).

### Option B — Extend `MapOperations()` to also handle blob-bearing entities

Pro: single entry point.
Con: `Operations.Http` would take a dependency on `Forge.ObjectStorage.Abstractions`;
mixes concerns in a package that must stay lightweight; violates the skip-and-warn design
of Entity ADR-0019.

## Decision

Option A. `Forge.ObjectStorage.Http` provides `MapObjectOperations()` as a standalone
`IEndpointRouteBuilder` extension.

---

### Discovery mechanism

`AddForgeObjectStorageHttp()` scans registered assemblies for classes annotated with
`[ObjectBearing]` and registers an `ObjectOperationDescriptor` per type in DI — the same
pattern that `OperationEndpointDescriptor` uses for `[OperationEndpoints]` types.

`MapObjectOperations()` reads all `ObjectOperationDescriptor` registrations and calls
`RegisterObjectEndpointsFor<T>(app, descriptor)` via reflection (same pattern as
`OperationEndpointsEndpointRouteBuilderExtensions`).

---

### Route convention

Base path: `api/objects/{path}` where `{path}` is derived from `[ObjectBearing]` or
defaults to `typeof(T).Name.ToLowerInvariant()` (the same defaulting rule as
`[OperationEndpoints]`).

| Verb | Route | Operation | Body / Response |
|------|-------|-----------|-----------------|
| `POST` | `api/objects/{path}` | Create metadata entity | JSON body → `OperationCreatedResponse(iri)` |
| `GET` | `api/objects/{path}` | List metadata entities | — → `OperationListResponse<TDto>` |
| `GET` | `api/objects/{path}?iri=…` | Read single metadata entity | — → `TDto` JSON |
| `PUT` | `api/objects/{path}/content?iri=…` | Upload binary content | `multipart/form-data` with a single `content` part → `OperationUpdatedResponse(iri)` |
| `GET` | `api/objects/{path}/content?iri=…` | Download binary content | — → `application/octet-stream` (or entity `ContentType`) |
| `DELETE` | `api/objects/{path}?iri=…` | Delete entity + blob | — → `OperationDeletedResponse()` |

The `/content` sub-resource suffix disambiguates binary from metadata routes without
ambiguity on the same path.

---

### Upload handler — multipart form, streaming, 3-step saga

The handler accepts `Content-Type: multipart/form-data` with a single named part
`content` (an `IFormFile` bound automatically by ASP.NET Core Minimal APIs). This allows
Bruno and browser clients to send real binary files using standard form encoding, and
avoids raw request-body reading which cannot carry per-part content-type metadata.

The route is decorated with `.DisableAntiforgery()` because: (a) the project registers
no antiforgery middleware, and (b) `IFormFile` parameters in Minimal APIs trigger
ASP.NET Core's automatic antiforgery metadata injection from .NET 8 onwards — without
`.DisableAntiforgery()` the endpoint throws at request time.

```
1.  Reject if Content-Length > ObjectStorageHttpOptions.MaxUploadBytes (default 256 MB)
     → 413 Payload Too Large (no body has been read)
2.  Load entity by IRI → 404 if null
3a. Generate newKey = "{TypeName}/{UuidV7()}"
3b. objectStore.UploadAsync(newKey, content.OpenReadStream(), content.ContentType)
     → on failure: return 500; nothing to roll back
4.  EntityTransaction.Update: entity.ObjectKey = newKey, entity.ContentType = content.ContentType
     → on AspectException: DeleteAsync(newKey); return 422 ENTITY_SHACL_VIOLATION
     → on other failure: DeleteAsync(newKey); return 500
5.  Best-effort DeleteAsync(previousObjectKey) if entity previously had a blob
6.  Return 200 OperationUpdatedResponse(iri)
```

`content.ContentType` carries the MIME type declared on the form part (`audio/wav`,
`image/png`, etc.). The request's top-level `Content-Type` is `multipart/form-data;
boundary=…` and is not used for blob storage.

`ObjectStorageHttpOptions.MaxUploadBytes` (the only configurable limit) applies to
`ctx.Request.ContentLength`, which covers the total multipart envelope; the actual
blob bytes are slightly smaller. For all practical purposes this is equivalent.

---

### Download handler — implicit aspect gate

```
1.  entity = await EntityRepository<T>.LoadAsync(iri)
     → 404 if null
     (Entity load goes through the full store chain including aspect enforcement —
      this is the aspect gate for the download. No separate aspect check needed.)
2.  if entity.ObjectKey is null → 404 "Content not yet uploaded"
3.  stream = await objectStore.DownloadAsync(entity.ObjectKey)
4.  return Results.Stream(stream, entity.ContentType ?? "application/octet-stream")
```

The load in step 1 uses the same `IEntityRepository<T>` registered by the entity-layer
DI helper, which is subject to the keyed store chain including `AspectEnforcingTransactionalStore`.
No additional aspect parameter is passed for reads — consistent with all other entity
`LoadAsync` reads in the platform.

---

### Delete handler — best-effort blob cleanup

```
1.  Load entity to obtain entity.ObjectKey  → 404 if null
2.  EntityTransaction.Delete(iri)           → 500 on failure; abort (blob untouched)
3.  await objectStore.DeleteAsync(entity.ObjectKey)
     → failure logged as warning; NOT propagated to caller
4.  return 200 OperationDeletedResponse()
```

The entity deletion is the authoritative act. Orphaned blobs after step 3 failure are
recoverable: the ADR-0021 `EventEmittingEntityStore` emits an `EntityChangedEnvelope<TDto>`
with `Operation = Deleted` and the final DTO (which includes `ObjectKey`). A background
cleanup consumer can use this event to retry blob deletion. No `207 Multi-Status` is
returned — simplicity and consistency with all other `DELETE` responses in the platform
(ADR-0017) take precedence.

---

### Dependency graph

```
Forge.ObjectStorage.Http
  → Forge.ObjectStorage.Abstractions   (IObjectStore, IObjectStoreProvider)
  → Forge.Entity                       ([ObjectBearing] attribute)
  → Forge.Operations.Http              (shared response contracts)
  → Forge.Execution.Http               (HeaderExecutionAspectIriProvider)
  → Forge.Repository.Transaction       (ITransactionalEntityStore, EntityTransaction)
  → Forge.Aspects.DependencyInjection  (AddForgeAspectsForKeyedStore)
  → Microsoft.AspNetCore.App
```

---

### `[ObjectBearing]` path convention

`[ObjectBearing]` does not currently have a `Path` parameter (unlike `[OperationEndpoints]`).
The path defaults to `typeof(T).Name.ToLowerInvariant()`. A `Path` parameter can be added
to `ObjectBearingAttribute` (Entity ADR-0019) in a follow-up without breaking existing
usages — the attribute property is optional.

## Consequences

- Callers use two separate `app.Map*` calls: `app.MapOperations()` for standard entities,
  `app.MapObjectOperations()` for blob-bearing entities. Forgetting the latter is caught
  at startup by the `LogWarning` emitted by `MapOperations()` (Entity ADR-0019).
- Binary download has no JSON envelope — a `Content-Type: application/octet-stream` (or
  entity-specific MIME type) response is returned directly. This is the only place in the
  platform where a GET endpoint intentionally bypasses `OperationListResponse<T>`.
- The 256 MB upload limit applies to the total multipart envelope (`Content-Length`),
  preventing unbounded memory growth. Large video files require configuration adjustment.
- The multipart `content` part carries the MIME type; clients must name the part
  `"content"` and set the part-level `Content-Type` accordingly. Bruno uses
  `body: multipart-form` with `content: @file(path) @contentType(mimeType)` (the
  `@contentType()` macro, NOT the pipe separator — `@file(path|mimeType)` splits on
  `|` producing multiple file-path arguments and does NOT set the content type;
  `body:multipartForm` camelCase is not a valid block keyword).
- Adding a `HEAD /content` endpoint, `Content-Disposition` filename, or pre-signed URL
  support are follow-up features that require no structural changes to the five routes
  defined here.
