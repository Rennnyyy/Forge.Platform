# 0016 — Geometry3D bundle download endpoint

- **Status**: accepted
- **Date**: 2026-05-16
- **Author**: agent

## Context

The existing `GET api/objects/geometry3d-nodes/content?iri=…` route (wired by
`MapObjectOperations()`) downloads one OBJ blob per request. After loading the big-car
sample (sample ADR-0015), the browser must trigger ≈ 4 000 individual downloads to
populate the Three.js 3D view. This is impractical for two reasons:

1. **Latency** — 4 000 sequential or parallel HTTP round-trips inflate load time by
   several seconds even over localhost.
2. **Browser limits** — browsers enforce per-origin connection limits; too many parallel
   fetches queue or fail.

A single endpoint that packages all `Geometry3D` blobs into one ZIP archive solves both
problems and mirrors a well-known CAD/PLM pattern (batch geometry export).

## Options

### Option A — Add to `Forge.ObjectStorage.Http` as a platform-level bulk-download route

Pro: reusable across any `[ObjectBearing]` entity type.
Con: introduces a collection-query dependency into a slice that currently only handles
single-entity routes; requires a new abstract scan API; broader scope.

### Option B — Add as a sample-level endpoint in `Application.Sample/Program.cs`

Pro: zero new slice surface; uses existing `IEntityStore`/`IObjectStoreProvider` DI
seams already present in the sample; isolates the demo feature.
Con: not reusable outside the sample without copy-paste.

## Decision

Option B — sample-level endpoint in `Program.cs`.

### Route

```
GET  api/objects/geometry3d-nodes/bundle
```

Branch isolation is provided automatically by the `UseBranchScope()` middleware via
the `X-Forge-BranchIri` request header; no query-parameter override is required.

### Behaviour

1. List all `Geometry3D` entities in the current branch using `EntityOperations.ListAsync<Geometry3D>()`.
2. For each entity that has a non-null `ObjectKey`, download the blob from
   `IObjectStore` (keyed `Geometry3D.ForgeObjectStoreKey`).
3. Write a `ZipArchive` (using `System.IO.Compression`) to the response stream.
   - Entry name: `{sanitisedName}_{index:D5}.obj`  
     where `sanitisedName` replaces all non-alphanumeric characters with `_`.
   - One additional entry `manifest.json` containing a JSON array of
     `{ iri, name, fileName }` objects for all included entries.
4. Return `Content-Type: application/zip`,
   `Content-Disposition: attachment; filename="geometry3d-bundle.zip"`.
5. Entities with `ObjectKey = null` (metadata-only nodes, never uploaded) are silently
   skipped; they do not appear in the manifest.

### Error handling

- `ObjectNotFoundException` for a key that exists in metadata but not in the store is
  silently skipped (best-effort, matching the orphan convention in `ObjectStorage.Http`
  ADR-0001).
- If no entities with content exist, the endpoint returns an empty ZIP (one manifest
  entry: an empty array).

### UI integration

`docs/car-demo-live.html` exposes the endpoint via a "Download All Geometries (ZIP)"
button in the new "Big Sample" section. The button fires a browser-native download by
navigating to the URL with the `X-Forge-BranchIri` baked in as a query parameter
workaround (since custom headers cannot be sent via `<a href>`).

Because binary ZIP content cannot be asserted as JSON in Bruno, the chapter-24 test
only asserts `res.status eq 200` and the `Content-Disposition` header contains
`geometry3d-bundle.zip`.

## Consequences

- No changes to `Forge.ObjectStorage.Http` or any other library slice.
- The bundle endpoint is registered after `app.MapObjectOperations()` in `Program.cs`.
- If a future requirement calls for a generalised bulk-download across entity types,
  this implementation can be extracted to `Forge.ObjectStorage.Http` as a first-class
  feature and the sample endpoint replaced by the library route — no change to clients.
