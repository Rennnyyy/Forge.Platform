# 0001 — `IObjectStore` interface — 4-method blob abstraction with convention-key staging

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Root ADR-0023 establishes `Forge.ObjectStorage.Abstractions` as the provider-agnostic
blob-store boundary. This ADR pins down the shape of the core interfaces.

Three requirements must be balanced:

1. **Minimal surface** — the interface must be implementable over S3, Azure Blob Storage,
   a local filesystem, and an in-process `ConcurrentDictionary` without deviation.
2. **Staging for safe upload** — the two-step Create-then-Upload workflow (Entity ADR-0019)
   requires that the HTTP layer uploads the blob to a _staging_ location before committing
   the `ObjectKey` to the entity. If the entity update fails, the staged blob must be
   cleaned up. The staging pattern must not require a new method on `IObjectStore`.
3. **Branch isolation is NOT a store concern** — blobs are globally keyed. Different
   branches holding the same entity type use different entity IRI instances (different
   branch graphs), which carry different `ObjectKey` values. The blob store itself is
   branch-unaware.

## Options

### Option A — 4-method interface + convention-key staging

`IObjectStore` exposes `UploadAsync`, `DownloadAsync`, `DeleteAsync`, `ExistsAsync`.
Staging is a key-naming convention: the HTTP layer uploads to `{finalKey}.staging`, then on
entity-update success calls `UploadAsync` with the final key (re-upload from a buffered
stream) and `DeleteAsync` on the staging key. No new method needed.

Pro: minimal interface; every adapter immediately implementable; staging implemented by
the HTTP layer using existing primitives.
Con: "staging" is implicit — there is no type-system guarantee that staging keys are
cleaned up. Orphaned staging keys require periodic garbage collection.

### Option B — Explicit staging methods (`UploadStagingAsync` / `PromoteStagingAsync`)

`IObjectStore` gains two extra methods. `PromoteStagingAsync` maps to S3
`CopyObject` + `DeleteObject`, which is atomic on the S3 side.

Pro: atomic promotion on cloud providers; staging lifecycle is explicit in the contract.
Con: adds two methods that every adapter must implement; in-process InMemory staging is
trivially a key rename; the "atomicity" benefit applies only to S3 and similar providers,
but `IObjectStore` is broker-agnostic.

### Option C — Streaming `PipeReader` / `PipeWriter` overloads

Replace `Stream` parameters with `System.IO.Pipelines` types.
Pro: backpressure-aware; avoids buffer copies in ASP.NET Core.
Con: `PipeReader`/`PipeWriter` are more complex to implement and test; the InMemory
implementation gains no benefit; deferred to a follow-up ADR.

## Decision

Option A for the interface shape. `Stream`-based I/O for v1 (Option C deferred).

### Interface definitions

```csharp
namespace Forge.ObjectStorage;

/// <summary>
/// Provider-agnostic binary object store.
/// Objects are identified by opaque, globally unique string keys.
/// Branch isolation is the caller's responsibility via key choice — the store itself
/// is branch-unaware.
/// </summary>
public interface IObjectStore
{
    /// <summary>
    /// Write <2 name="content"/> under <paramref name="objectKey"/>.
    /// Overwrites any existing object with the same key.
    /// The caller is responsible for rewinding the stream before calling.
    /// </summary>
    ValueTask UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a readable stream over the stored content.
    /// The caller owns the returned stream and must dispose it.
    /// Throws <see cref="ObjectNotFoundException"/> when the key does not exist.
    /// </summary>
    ValueTask<Stream> DownloadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the object. No-op when the key does not exist.
    /// </summary>
    ValueTask DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>Returns <see langword="true"/> when the key exists in the store.</summary>
    ValueTask<bool> ExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves a named <see cref="IObjectStore"/> by DI key string.
/// Useful when an application registers multiple stores (e.g. one per media type).
/// </summary>
public interface IObjectStoreProvider
{
    IObjectStore GetStore(string storeKey);
}
```

`ObjectNotFoundException` is a new exception type in `Forge.ObjectStorage`.

### Convention-key staging protocol — used by `Forge.ObjectStorage.Http`

The staging protocol is a caller convention, not an interface method. The HTTP upload
saga (see `Forge.ObjectStorage.Http` ADR-0001) follows these steps:

| Step | Action | Rollback on failure |
|------|--------|---------------------|
| 1 | `UploadAsync("{finalKey}.staging", stream, contentType)` | — |
| 2 | `EntityTransaction.Update(objectKey = finalKey, ...)` | `DeleteAsync("{finalKey}.staging")` |
| 3 | `UploadAsync(finalKey, stream, contentType)` | `DeleteAsync(finalKey)` if written; staging key already cleaned up by step 2 success |
| 4 | `DeleteAsync("{finalKey}.staging")` | — (idempotent) |

Because step 3 re-uploads from a buffered `MemoryStream` (the request body is buffered at
the HTTP layer for `ExistsAsync`/checksum purposes), no read-back from the staging key is
needed. The staging key suffix `.staging` is a reserved suffix — callers must not use it
as the final key.

### Key generation

`ObjectKey` values are generated by the HTTP layer as `{TypeName}/{UuidV7()}` at upload
time (e.g. `document/018f4c...`). The `TypeName` segment aids human-readable debugging
and supports prefix-based listing on S3-compatible stores. `UuidV7` provides
time-ordered uniqueness without a coordination step.

### InMemory implementation contract

`InMemoryObjectStore` in `Forge.ObjectStorage.InMemory`:
- Backed by `ConcurrentDictionary<string, (byte[] Data, string ContentType)>`.
- `UploadAsync` reads the stream to a `byte[]` and stores it; the stream does not need to
  be seekable.
- `DownloadAsync` returns a `new MemoryStream(storedBytes)` (caller owns and disposes).
- `DeleteAsync` calls `TryRemove`; no-op if absent.
- `ExistsAsync` calls `ContainsKey`.
- Thread-safe by `ConcurrentDictionary` semantics.

`InMemoryObjectStoreProvider` wraps a `ConcurrentDictionary<string, InMemoryObjectStore>`,
creating a new store on first `GetStore(key)` call.

## Consequences

- `IObjectStore` is implementable by any blob backend in ~20 lines; no SDK coupling.
- The staging suffix `.staging` is a reserved convention; a follow-up ADR may promote it
  to a named constant in `Forge.ObjectStorage.Abstractions` to prevent duplication.
- S3 / Azure adapters are out of scope for this ADR; they implement the same interface
  and live in separate packages.
- `PipeReader`/`PipeWriter` overloads are deferred; the interface has no `Stream`-specific
  members that would prevent adding `IObjectStorePipesExtensions` later.
- Orphaned staging keys (from process crashes between steps 1 and 2) accumulate with the
  `.staging` suffix; a cleanup task keyed on entity delete events (ADR-0021) is the
  recommended strategy, but is out of scope for v1.
