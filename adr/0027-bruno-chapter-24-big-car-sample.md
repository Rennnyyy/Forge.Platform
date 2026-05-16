# 0027 — Bruno collection extended to 24 chapters (big-car sample + bundle download)

- **Status**: accepted; amends [0026](0026-bruno-chapter-23-geometry-snapshot.md)
- **Date**: 2026-05-16
- **Author**: agent

## Context

ADR-0026 documented the Bruno integration-test collection through chapter 23
(`23-geometry-snapshot`). Two new capabilities were added:

1. `PopulateBigCarDemoHandler` — seeds a 7-level, 6 000-leaf, 4 000-geometry big-car
   product tree (sample ADR-0015).
2. `GET api/objects/geometry3d-nodes/bundle` — packages all `Geometry3D` blobs into a
   single ZIP archive (sample ADR-0016).

Both are covered by a new Bruno chapter 24.

## Decision

The story-chapter conventions from ADR-0013 remain in force unchanged.

One chapter is appended to the ADR-0026 inventory:

| Chapter | Folder | What it demonstrates |
|---------|--------|----------------------|
| 24 | `24-big-car-sample/` | Big-scale car demo: `PopulateBigCarDemoHandler` seeds 7 576 structure nodes across 7 levels, 4 000 unique Geometry3D nodes, and 10 000 Geometry3DUsage placements; `GET api/objects/geometry3d-nodes/bundle` downloads all blobs as a single ZIP archive. See sample ADR-0015 and ADR-0016. |

The corresponding integration-test method added to `BrunoIntegrationTests.cs`:

| Test name | Chapter folder |
|-----------|----------------|
| `Bruno_24_big_car_sample_requests_all_pass` | `24-big-car-sample/` |

## Consequences

- The `01-populate-big-car.bru` request specifies `http { timeout: 120000 }` because
  the populate operation creates ≈ 29 000 entities and may exceed the default 10-second
  CLI timeout.
- No earlier chapters are structurally affected.
