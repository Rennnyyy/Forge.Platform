# Architecture Decision Records — Forge.Branch

Slice-local decisions for the Branch library. Read after the [root ADRs](../../../adr/) and before making changes to `Forge.Branch`.

Format and rules: see [root ADR README](../../../adr/README.md).

## Index

- [0001 — Branch entity and management graph](0001-branch-entity-and-management-graph.md)
- [0002 — Snapshot entity, immutability guard, and version bounds](0002-snapshot-entity-and-immutability-guard.md)
- [0003 — BranchSeedingService: orchestration for seeded branch and snapshot creation](0003-branch-seeding-service.md)
- [0004 — Entity-level graph diff engine (`IBranchDiffEngine`)](0004-entity-graph-diff-engine.md)
- [0005 — Branch lineage and the `DerivedFrom` predicate](0005-branch-lineage-derived-from.md)
- [0006 — Merge planner and topological sort (`IMergePlanner`)](0006-merge-planner.md)
- [0007 — Branch merge service (`BranchMergeService`)](0007-branch-merge-service.md)
