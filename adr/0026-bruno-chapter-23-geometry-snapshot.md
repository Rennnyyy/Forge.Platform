# 0026 — Bruno collection extended to 23 chapters (geometry snapshot demo)

- **Status**: accepted; amends [0016](0016-bruno-chapter-expansion.md)
- **Date**: 2026-05-16
- **Author**: agent

## Context

ADR-0016 documented the Bruno integration-test collection through chapter 22
(`22-car-build`). A new chapter that demonstrates Structure ADR-0006
(`Usage.SnapshotIri` per-edge branch annotation) was added to the collection;
ADR-0016's chapter inventory is now one chapter out of date.

## Decision

The story-chapter conventions from ADR-0013 remain in force unchanged.

One chapter is appended to the ADR-0016 inventory:

| Chapter | Folder | What it demonstrates |
|---------|--------|----------------------|
| 23 | `23-geometry-snapshot/` | Geometry snapshot demo: `Usage.SnapshotIri` seeds a v1.0 steel-frame outline into a separate named graph; the configured-tree response carries `snapshotBranchIri` on the SteelFrame node; a scoped `GET /api/entities/geometry-nodes` request verifies the v1 geometry is isolated in the snapshot branch. See Sample ADR-0014 and Structure ADR-0006. |

The corresponding integration-test method added to `BrunoIntegrationTests.cs`:

| Test name | Chapter folder |
|-----------|----------------|
| `Bruno_23_geometry_snapshot_requests_all_pass` | `23-geometry-snapshot/` |

## Consequences

- Chapter 22's `01-populate-car-demo.bru` geometry count assertions are updated
  from `eq 10` to `eq 11` (one extra node + usage seeded into the snapshot branch).
- No earlier chapters are structurally affected.
