# 0015 — Big-car sample: 7-level tree, 6 000 leaf nodes, 4 000 geometry shapes

- **Status**: accepted
- **Date**: 2026-05-16
- **Author**: agent

## Context

The existing `PopulateCarDemoHandler` creates 28 structural nodes + 27 Usage edges
+ 9 `Geometry3D` nodes + 22 `Geometry3DUsage` edges. That scale is ideal for showing
all condition types in a single quick call but does not exercise:

1. **Deep hierarchies** — the tree is only 6 levels deep (Car → Chassis → Steel Frame is
   already 3 levels; the actual depth reaches at most 5 hops).
2. **Geometry reuse** — the same OBJ shape is used at most twice (wheel-disc for four
   placements). Real product data reuses hundreds of standard-part shapes thousands of times.
3. **Mass download need** — with 4 000 unique `Geometry3D` blobs, triggering 4 000
   individual `GET api/objects/geometry3d-nodes/content` requests is impractical. A single
   bundle endpoint is required (documented in sample ADR-0016).

## Decision

Add a second populate capability: `PopulateBigCarDemoHandler` keyed
`car.demo.big.populate` at `POST api/capabilities/car/demo/big/populate`.

### Tree structure (7 levels including root, all edges unconditional)

| Level | Count | Description |
|-------|-------|-------------|
| 0 | 1 | Root — "Big Car Platform Family" |
| 1 | 3 | Platform groups (Alpha, Beta, Gamma) |
| 2 | 12 | Vehicle segments (Sedan, SUV, Coupe, Pickup × 3 platforms) |
| 3 | 60 | Build configurations (5 configs per segment) |
| 4 | 300 | Subsystem groups (Structural, Powertrain, Electrical, Interior, Chassis per config) |
| 5 | 1 200 | Module groups (Module-A/B/C/D per subsystem) |
| 6 | 6 000 | Leaf components (Component-01/02/03/04/05 per module) |

Total: **7 576 nodes**, **7 575 edges**, depth = 7 levels.

All edges use `ConditionSet.Empty` — the purpose is to demonstrate structural scale,
not condition complexity (which is already the responsibility of the existing small
car demo and Bruno chapter 22).

### Geometry layer

| Item | Count |
|------|-------|
| Unique `Geometry3D` nodes | 4 000 |
| `GeometryUsage3D` placements (primary, one per structure node) | 7 576 |
| `GeometryUsage3D` placements (secondary, first 2 424 leaf nodes) | 2 424 |
| **Total `GeometryUsage3D`** | **10 000** |

The pool of 4 000 `Geometry3D` nodes is created before tree construction. Each node
cycles through 8 standard-part OBJ shapes (box, plate, beam, bracket, rod, housing,
small-cube, post) — giving visual variety with minimal byte cost (each OBJ is approx.
220 bytes of 8-vertex box geometry). Average reuse factor across the pool: 2.5×.

The secondary placements for the first 2 424 leaf nodes use an offset matrix
(translate +0.5 on X) so the two parts placed on those nodes do not overlap in 3D space.

### Response

`PopulateBigCarDemoResponse` carries:
- `treeNodeCount: 7576`
- `leafNodeCount: 6000`
- `treeEdgeCount: 7575`
- `geometry3dNodeCount: 4000`
- `geometry3dUsageCount: 10000`
- `rootIri` — IRI of the root "Big Car Platform Family" node

### Bruno chapter

| Chapter | Folder | Tests |
|---------|--------|-------|
| 24 | `24-big-car-sample/` | `01-populate-big-car.bru`, `02-query-big-car-tree.bru`, `03-download-geometry-bundle.bru` |

The populate request uses a per-request `http { timeout: 120000 }` block because the
operation creates ≈ 29 000 entities and may take more than the default 10-second timeout.

See root ADR-0027 for the Bruno inventory update.

## Consequences

- The existing small-car demo (chapter 22, `PopulateCarDemoHandler`) is untouched.
- The live demo page (`docs/car-demo-live.html`) gains a separate "Big Sample" section with
  statistics panel and a "Download All Geometries (ZIP)" button (sample ADR-0016).
- The InMemory backend handles this scale without external infrastructure.
- Calling `POST api/capabilities/car/demo/big/populate` twice produces two independent
  trees (not idempotent), same as the small demo.
