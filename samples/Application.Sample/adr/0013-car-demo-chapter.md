# 0013 — Car build demo chapter (Bruno 22-car-build)

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

Chapter 21 introduces the `Forge.Structure` slice through 28 individual HTTP calls. A companion chapter is needed that:

1. Shows all three condition types exercised together in one realistic domain model.
2. Seeds the complete 150 % tree in a single capability call.
3. Demonstrates that **milestones are time-effectivity windows**, not separate sub-trees.

## Design decisions

### 1. One tree, real conditions

The Demo Car product structure is a **single DAG** rooted at the `Car` node. Structural variation is expressed through `ConditionSet` entries on `Usage` edges — not through topology duplication.

Two orthogonal axes of variance are modelled:

| Axis | Condition type | Dimension / bounds |
|------|---------------|--------------------|
| Milestone era | `TimeCondition` | initial: until 2025-12-31; update-1: from 2026-01-01 |
| Drivetrain | `EnumerationOptionCondition` | `ev` / `ice` |
| Interior trim | `EnumerationOptionCondition` | `base` / `sport` / `luxury` |

`ConditionSet` semantics are **AND**: the `Interior → Luxury Interior` edge carries both
`trim=luxury` AND `ValidFrom=2026`, requiring both to be satisfied simultaneously.

Both enumeration dimensions use `IsRequired = false` (open-world): omitting a dimension
key from the query returns all values for that axis — the 150 % view for that dimension.

### 2. Milestone-era model

Milestones are time windows, not structural forks:

| Element | Condition | Meaning |
|---------|-----------|---------|
| Aluminium Space Frame | `TimeCondition(ValidFrom=2026)` | Only available from update-1 era |
| Thermal Management | `TimeCondition(ValidFrom=2026)` | Battery cooling added in update-1 |
| Rear Motor | `TimeCondition(ValidFrom=2026)` | AWD option added in update-1 |
| Turbocharger | `TimeCondition(ValidFrom=2026)` | Forced induction added in update-1 |
| Manual Clutch | `TimeCondition(ValidFrom=2026)` | Gearbox sub-options added in update-1 |
| Automatic Transmission | `TimeCondition(ValidFrom=2026)` | Gearbox sub-options added in update-1 |
| Luxury Interior | `EnumerationOptionCondition(trim=luxury)` AND `TimeCondition(ValidFrom=2026)` | Combined AND condition |
| Race Edition Pack | `TimeCondition(ValidTo=2025-12-31)` | Discontinued after initial era |

### 3. Tree summary (28 nodes, 27 edges)

```
Car
├── Chassis
│   ├── Steel Frame
│   └── Aluminium Space Frame  [ValidFrom=2026]
├── Powertrain
│   ├── EV Package             [drivetrain=ev]
│   │   ├── Battery Pack
│   │   │   ├── Battery Management System
│   │   │   └── Thermal Management     [ValidFrom=2026]
│   │   └── E-Machine
│   │       ├── Front Motor
│   │       └── Rear Motor             [ValidFrom=2026]
│   └── ICE Package            [drivetrain=ice]
│       ├── Combustion Engine
│       │   └── Turbocharger           [ValidFrom=2026]
│       └── Gearbox
│           ├── Manual Clutch          [ValidFrom=2026]
│           └── Automatic Transmission [ValidFrom=2026]
├── Interior
│   ├── Base Interior          [trim=base]
│   │   └── Cloth Seats
│   ├── Sport Interior         [trim=sport]
│   │   └── Sport Seats
│   └── Luxury Interior        [trim=luxury AND ValidFrom=2026]
│       ├── Leather Seats
│       └── Panorama Roof
└── Race Edition Pack          [ValidTo=2025-12-31]
    └── Roll Cage
```

### 4. Node counts per query

| Era | Drivetrain | Trim | Nodes |
|-----|-----------|------|-------|
| 2025 (initial) | open | open | 19 |
| 2026 (update-1) | open | open | 26 |
| 2025 | ev | sport | 14 |
| 2025 | ice | base | 12 |
| 2026 | ev | luxury | 16 |
| 2026 | ice | sport | 14 |

### 5. Bruno chapter 22 (8 requests)

| Seq | File | Configuration | Nodes |
|-----|------|---------------|-------|
| 01 | `01-populate-car-demo.bru` | Seeds 28 nodes + 27 conditioned edges; returns dimension IRIs | — |
| 02 | `02-query-initial-era-150pct.bru` | no options · date=2025 | 19 |
| 03 | `03-query-update1-era-150pct.bru` | no options · date=2026 | 26 |
| 04 | `04-query-initial-ev-sport.bru` | drivetrain=ev · trim=sport · date=2025 | 14 |
| 05 | `05-query-initial-ice-base.bru` | drivetrain=ice · trim=base · date=2025 | 12 |
| 06 | `06-query-update1-ev-luxury.bru` | drivetrain=ev · trim=luxury · date=2026 | 16 |
| 07 | `07-query-update1-ice-sport.bru` | drivetrain=ice · trim=sport · date=2026 | 14 |
| 08 | `08-read-car-node.bru` | CRUD entity read of Car root node | — |

### 6. ECharts documentation

`docs/car-demo.html` has three filter dropdowns (era, drivetrain, trim). The JavaScript
applies the same condition logic as `ConditionSet.IsSatisfiedBy` to the static data,
greys out excluded nodes, and shows the reachable-node count — matching the API response
for the corresponding query.

### 7. Well-known enumeration value IRIs

These are plain string constants, not entity IRIs:

| Dimension | Value | IRI |
|-----------|-------|-----|
| Drivetrain | EV | `urn:forge:car:drivetrain/ev` |
| Drivetrain | ICE | `urn:forge:car:drivetrain/ice` |
| Trim | Base | `urn:forge:car:trim/base` |
| Trim | Sport | `urn:forge:car:trim/sport` |
| Trim | Luxury | `urn:forge:car:trim/luxury` |

## Consequences

- Chapter 22 is standalone; it does not depend on chapter 21.
- All three condition types (`TimeCondition`, `EnumerationOptionCondition`, combined AND)
  are exercised in the same tree — making this the primary end-to-end showcase for the
  condition system.
- `PopulateCarDemoHandler` creates two `Dimension` entities and 28 `Node` entities per
  call (not idempotent — calling twice creates a second independent tree).
- Root ADR-0016 is updated with the chapter 22 entry and the new `[SkippableFact]` name.
