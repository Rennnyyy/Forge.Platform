# 0011 — Track masters object-storage demo (Bruno 19-track-masters)

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Root ADR-0023 introduces three new object-storage slices
(`Forge.ObjectStorage.Abstractions`, `Forge.ObjectStorage.InMemory`,
`Forge.ObjectStorage.Http`) and the `[ObjectBearing]` attribute (Entity ADR-0019).
`ObjectStorage.Http` ADR-0001 defines the five-verb resource model and the
multipart-form upload contract.

`Application.Sample` had no runtime demonstration of this mechanism. No entity in
the sample was annotated with `[ObjectBearing]`, `MapObjectOperations()` was not
called, and no Bruno chapter exercised the binary upload/download surface.

## Decision

Add a `TrackMaster` entity that represents the master recording of a music track and
wire it into the sample app via `MapObjectOperations()`.

### `TrackMaster` entity model

```csharp
[ObjectBearing("track-masters", StoreKey = "track-masters-store")]
public class TrackMaster : Entity
{
    public string Title    { get; set; } = "";
    public string Artist   { get; set; } = "";
    public int    Year     { get; set; }
    // injected by MapObjectOperations:
    public string? ObjectKey    { get; set; }
    public string? ContentType  { get; set; }
}
```

The path `"track-masters"` drives the route prefix `api/objects/track-masters`.
The `StoreKey = "track-masters-store"` is the keyed store registration name used by
`AddForgeObjectStorageInMemory()` and aspect DI.

### Changes to `Program.cs`

1. `AddForgeObjectStorageInMemory()` — registers the in-memory blob store.
2. `AddForgeObjectStorageHttpFromAssemblyContaining<TrackMaster>()` — scans for
   `[ObjectBearing]` types and registers `ObjectOperationDescriptor` in DI; also
   registers the managed-entity store key and aspect wiring.
3. `app.MapObjectOperations()` — wires all five routes for `TrackMaster`.

### Aspect registrations

Two aspects are registered to demonstrate the aspect pipeline on object operations:

| Aspect IRI | Type | What it enforces |
|---|---|---|
| `urn:forge:aspects:operation:track-master-write-v1` | Local SHACL | Requires `year ≥ 1900` on upload |
| `urn:forge:aspects:operation:track-master-lock-v1` | Context WHERE | Rejects re-upload of any entity that already has an `objectKey` set in the triple store |

### New Bruno chapter `19-track-masters/`

| File | seq | What it demonstrates |
|------|-----|----------------------|
| `00-setup-env.bru` | 1 | Sets `trackMasterIri` env var from a `POST api/objects/track-masters` create |
| `01-create.bru` | 2 | `POST api/objects/track-masters` → 201 `OperationCreatedResponse` |
| `02-read.bru` | 3 | `GET api/objects/track-masters?iri=…` → 200 metadata JSON |
| `03-list.bru` | 4 | `GET api/objects/track-masters` → 200 `OperationListResponse` |
| `04-download-before-upload.bru` | 5 | `GET …/content?iri=…` → 404 (no blob yet) |
| `05-upload-content.bru` | 6 | `PUT …/content?iri=…` with `multipart/form-data`, part `content: @file(assets/sample-track-master.wav) @contentType(audio/wav)` and aspect `track-master-write-v1` → 200 `OperationUpdatedResponse` |
| `06-re-upload-blocked.bru` | 7 | Same PUT with `track-master-lock-v1` → 422 `ENTITY_SHACL_VIOLATION` |
| `07-download.bru` | 8 | `GET …/content?iri=…` → 200 `audio/wav` stream |
| `08-download-gate-blocked.bru` | 9 | `GET …/content` with a download-gate aspect → 422 `ENTITY_ASPECT_VIOLATION` |
| `09-branch-setup.bru` | 10 | `POST api/branches` → branch IRI into `trackBranchIri` |
| `10-branch-create.bru` | 11 | `POST api/objects/track-masters` with `X-Forge-BranchIri` header |
| `11-branch-upload.bru` | 12 | `PUT …/content` with `X-Forge-BranchIri` header |
| `12-branch-download.bru` | 13 | `GET …/content` with `X-Forge-BranchIri` header → 200 |
| `13-branch-isolation.bru` | 14 | `GET api/objects/track-masters` without header — branch entity absent |
| `14-branch-delete.bru` | 15 | `DELETE api/branches?iri={{trackBranchIri}}` → 200 |
| `15-delete-entity-and-blob.bru` | 16 | `DELETE api/objects/track-masters?iri=…` → 200; subsequent `GET …/content` → 404 |
| `16-verify-blob-deleted.bru` | 17 | `GET api/objects/track-masters/content?iri=…` → 404 confirms combined delete |
| `17-teardown.bru` | 18 | `DELETE api/objects/track-masters?iri=…` cleanup of remaining entities |

### `assets/sample-track-master.wav`

An 852-byte valid WAV file (8 kHz, mono, 8-bit PCM, 0.1 s silence) committed under
`samples/Application.Sample/bruno/assets/`. Bruno loads it as the multipart file part
for upload requests. No audio framework or external tool is required; the file is
generated from raw PCM bytes with a hand-constructed RIFF/WAV header.

## Consequences

- Any Bruno chapter that exercises `PUT …/content` must use `body: multipart-form`
  (the bru v2 block keyword — `body:multipart-form` with a hyphen) with
  `content: @file(path) @contentType(mimeType)`. The `@contentType()` macro sets the
  part content-type; the pipe separator `@file(path|mimeType)` does NOT — it produces
  multiple file-path arguments and the second element is treated as another file path.
- The Bruno integration test `Bruno_19_track_masters_requests_all_pass` in
  `Application.Sample.Tests` is skipped when `npx` is not on `PATH`
  (consistent with all other Bruno chapters — ADR-0012).
- `MapObjectOperations()` must be called in addition to `MapOperations()`; calling only
  one silently omits the other's routes. The `LogWarning` from `MapOperations()` when
  it skips `[ObjectBearing]` types serves as the reminder.
