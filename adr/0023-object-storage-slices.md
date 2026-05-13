# 0023 — Object Storage slices (`Forge.ObjectStorage.Abstractions`, `Forge.ObjectStorage.InMemory`, `Forge.ObjectStorage.Http`)

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Entities in Forge.Platform are persisted as RDF triples and exposed over JSON HTTP.
Some domain entities are primarily metadata wrappers for a single binary object (a file,
a document, an image) that lives in an external blob store. No abstraction exists for
writing or reading binary content, and the standard `MapOperations()` code-generated
surface (ADR-0017/ADR-0019) is JSON-only and cannot accommodate streaming upload or
download.

Three concerns are entangled and must each be addressed cleanly:

1. **Store abstraction** — a provider-agnostic `IObjectStore` that InMemory, S3, Azure
   Blob, and any future backend can implement without coupling to each other.
2. **Entity marker** — a way for the generator and `MapOperations()` to recognise that an
   entity type owns a blob and must be routed through a different HTTP surface.
3. **HTTP transport** — streaming upload and binary download as a first-class, managed
   HTTP layer that satisfies ADR-0017/ADR-0019 obligations (shared response contracts,
   aspect threading, keyed store registration).

Messaging (ADR-0020/ADR-0021/ADR-0022) is intentionally excluded from the binary
channel: uploading bytes as a fire-and-forget async command is not meaningful (upload
must complete before the caller can confirm the entity has content), and downloading
bytes as an async event stream does not map to the blob-access pattern. Metadata change
events (entity created, ObjectKey updated via upload, entity deleted) are emitted by the
ADR-0021 `EventEmittingEntityStore` decorator as for any other entity — no special
event treatment is required.

## Options

### Option A — Each consuming slice owns its own blob primitives

Pro: zero new projects.
Con: duplicates stream I/O interfaces; S3 and InMemory adapters cannot be substituted;
each HTTP layer reinvents the upload saga; blob orphan cleanup has no shared signal.

### Option B — Shared `IObjectStore` in `Forge.Repository`

Pro: no new project.
Con: `Forge.Repository` is an RDF-store abstraction; embedding byte-stream I/O there
blurs layering and adds a dependency on blob SDKs to a semantics-oriented package.

### Option C — Three dedicated slices mirroring the Messaging pattern (ADR-0020)

Pro: clean separation; InMemory backend covers tests with zero external processes; HTTP
layer is a first-class managed-entity slice subject to the full ADR-0017/ADR-0019 protocol.
Con: three new projects.

## Decision

Option C.

| Slice | Target | Purpose |
|-------|--------|---------|
| `Forge.ObjectStorage.Abstractions` | `net10.0` | `IObjectStore`, `IObjectStoreProvider`. Zero blob-SDK dependency. |
| `Forge.ObjectStorage.InMemory` | `net10.0` | `ConcurrentDictionary<string, byte[]>` implementation for tests and samples. |
| `Forge.ObjectStorage.Http` | `net10.0` | Managed-entity HTTP layer — `MapObjectOperations()`. |

The `[ObjectBearing]` attribute that marks an entity as blob-owning lives in
`Forge.Entity.Attributes` (Entity slice ADR-0019), keeping `Forge.Entity` free of any
object-storage dependency. `Forge.ObjectStorage.Abstractions` and `Forge.ObjectStorage.Http`
reference `Forge.Entity`; the reverse is not true.

## Consequences

- Applications that need object storage add `AddForgeObjectStorageInMemory()` for tests
  and swap in an S3 adapter for production — no code change in the domain layer.
- The `[ObjectBearing]` attribute skips the entity from standard `MapOperations()`
  registration; `MapObjectOperations()` picks it up instead.
- The slice naming follows ADR-0004 (`Forge.<Library>`) and ADR-0008 (no `Entity.` prefix).
- Slice-local decisions are documented under `src/ObjectStorage.Abstractions/adr/` and
  `src/ObjectStorage.Http/adr/` respectively.
- `Forge.ObjectStorage.InMemory` has no ADR folder in v1; its behaviour is fully
  specified by `Forge.ObjectStorage.Abstractions` ADR-0001.
