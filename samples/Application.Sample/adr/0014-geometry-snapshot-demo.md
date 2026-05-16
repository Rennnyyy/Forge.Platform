# 0014 — Geometry snapshot demonstration chapter (Bruno 23-geometry-snapshot)

- **Status**: accepted
- **Date**: 2026-05-16
- **Author**: agent

## Context

Structure ADR-0006 introduced `Usage.SnapshotIri` and `StructureNodeDto.SnapshotBranchIri`
as the mechanism for per-edge branch annotation in the configured tree. Without a
live demonstration in the sample application, the feature has no running proof and
no Bruno integration coverage.

A minimal, self-contained demo is needed that:

1. Seeds two versions of the same geometry (a simplified "v1" outline and the
   current "v2" outline) into separate named graphs.
2. Wires the `Usage(Chassis → SteelFrame)` edge with `SnapshotIri` pointing to
   the v1 geometry branch.
3. Queries the configured tree and verifies that the `SteelFrame` node (and its
   structural children) carries `snapshotBranchIri` in the response.
4. Lists geometry nodes from both branches to confirm that exactly one geometry
   node is visible per branch for the steel frame.

## Decisions

### 1. Domain choice

The existing car demo (`PopulateCarDemoHandler`) already owns all car-domain
structure nodes, so it is the natural place to seed the snapshot-geometry data.
No new domain entities are required.

### 2. Snapshot branch IRI

`"https://forge-it.net/branches/geometry-v1"` is used as the named graph IRI for
the v1 geometry. This is a plain string constant, not a registered `Snapshot`
entity (the car demo uses InMemory store which supports lazy named-graph creation).
The name follows the existing branch IRI format
(`https://forge-it.net/branches/{name}`) established by Branch ADR-0001 so that
the `BranchScopeMiddleware` can route `X-Forge-BranchIri` reads to it without
additional server-side registration.

**Not** using a `Snapshot : Branch` entity is a deliberate simplification for the
demo context. A follow-up ADR could introduce snapshot entity management for
production use cases.

### 3. v1 geometry appearance

`SvgBodyV1` is a single dashed-stroke rectangle (no axle guide lines). This is
visibly distinct from the v2 `SvgBodyBase` (solid stroke + axle dashes) and
clearly conveys "an older simpler version".

### 4. Seeding location

The v1 geometry and its `GeometryUsage` placement edge are saved inside a
`using (BranchScope.Use(GeometryV1SnapshotBranchIri))` block inside
`PopulateCarDemoHandler.HandleAsync`. The main-branch v2 geometry for the
steel frame is populated outside that block (unchanged from previous behaviour).

### 5. `PopulateCarDemoResponse` extensions

Two new fields are added:

| Field | Purpose |
|-------|---------|
| `SteelFrameIri` | Allows Bruno to find the SteelFrame node in the tree by IRI for precise `snapshotBranchIri` assertion. |
| `GeometrySnapshotBranchIri` | The constant `"https://forge-it.net/branches/geometry-v1"` echoed in the response so Bruno scripts can use it without hard-coding. |

### 6. Updated geometry counts

`GeometryNodeCount` increases from 10 to 11 and `GeometryUsageCount` from 10 to 11
because one extra geometry node and one extra `GeometryUsage` edge are seeded into
the snapshot branch. Chapter 22's Bruno assertion file is updated accordingly.

### 7. Bruno chapter structure

Chapter 23 (`23-geometry-snapshot/`) contains three requests:

| File | What it tests |
|------|--------------|
| `01-populate-car-demo.bru` | Calls `car/demo/populate`; saves `steelFrameIri` and `geomSnapshotBranchIri`. |
| `02-query-tree-snapshot-annotated.bru` | Calls `structure/configured-tree/get`; verifies `SteelFrame` carries `snapshotBranchIri`; verifies root does not. |
| `03-list-geometry-from-snapshot.bru` | `GET /api/entities/geometry-nodes` with snapshot branch header; verifies exactly 1 node returned (the v1 geometry). |

## Consequences

- `PopulateCarDemoHandler` grows two new response fields and one constant.
- Chapter 22's `01-populate-car-demo.bru` geometry count assertions are updated
  from `eq 10` to `eq 11` for both counts.
- The new `SteelFrameIri` and `GeometrySnapshotBranchIri` fields are additive; no
  earlier Bruno scripts are broken by their presence.
- The running demo provides a live, verifiable proof of Structure ADR-0006.
