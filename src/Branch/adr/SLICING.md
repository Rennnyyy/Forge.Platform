# Forge.Branch — slice folder structure

This document satisfies the ADR-0010 sub-folder requirement for the `Forge.Branch` slice.

## Flat root (`src/Branch/`)

Entities, guards, options, startup services, and DI wiring that collectively constitute
the branch-management domain. These types are cohesive and small; keeping them flat makes
the principal concerns immediately visible.

| File | Purpose |
|------|---------|
| `Branch.cs` | `Branch` entity: named-graph entity, identity = slug, `DerivedFrom` lineage |
| `BranchDefault.cs` | Static `BranchDefault.BranchIri` populated at DI registration time |
| `BranchGuardedTransactionalStore.cs` | Decorator: blocks writes that would delete the default branch or its graph |
| `BranchOptions.cs` | Configuration: default branch IRI, management graph IRI |
| `BranchProtectionViolationException.cs` | Thrown by `BranchGuardedTransactionalStore` |
| `BranchSeedingService.cs` | Application service: create seeded branches and snapshots |
| `DataSnapshotGuardedTransactionalStore.cs` | Decorator: blocks entity writes into frozen snapshot graphs (data store) |
| `DefaultBranchStartupService.cs` | `IHostedService`: upserts the default branch entity at startup |
| `ISnapshotFrozenSetInvalidator.cs` | Interface for refreshing the frozen-IRI set after snapshot mutations |
| `Snapshot.cs` | `Snapshot : Branch` entity: adds `SnapshotAt` + SemVer properties |
| `SnapshotGuardedTransactionalStore.cs` | Decorator + frozen-set cache: blocks writes into frozen snapshot graphs (management store) |
| `SnapshotImmutabilityViolationException.cs` | Thrown by snapshot guards |
| `SnapshotStartupService.cs` | `IHostedService`: loads existing snapshots into the frozen-set cache at startup |
| `SnapshotVersionConflictException.cs` | Thrown by `BranchSeedingService` on SemVer collision |

## `Merge/` sub-folder (`src/Branch/Merge/`)

All types that implement the diff-and-merge pipeline introduced in Branch ADR-0004 through
ADR-0007. Grouping them here keeps the merge concern self-contained and makes the boundary
with the existing management types explicit.

| File | Purpose |
|------|---------|
| `EntityDeltaKind.cs` | Enum: `Added` / `Modified` |
| `EntityDeltaEntry.cs` | Value object carrying entity IRI, type IRI, and kind |
| `EntityGraphDelta.cs` | Carrier for the full diff result (list of entries + graph IRIs) |
| `IBranchDiffEngine.cs` | Public interface: `ComputeDiffAsync` |
| `BranchDiffEngine.cs` | Implementation: multi-graph SPARQL (GraphDB) and scoped single-graph fallback (InMemory) |
| `IMergePlanner.cs` | Public interface: `PlanAsync` |
| `MergePlanner.cs` | Implementation: hydration, create-vs-update check, Kahn topological sort |
| `MergePlanCycleException.cs` | Thrown when a circular owning dependency is detected |
| `MergePlanHydrationException.cs` | Thrown when source hydration fails |
| `MergePlanUnresolvableTypeException.cs` | Thrown when an rdf:type IRI has no registered mapper |
| `BranchMergeResult.cs` | Return value of `BranchMergeService.MergeAsync` |
| `BranchMergeService.cs` | Orchestrating service: diff → plan → transact |

## `DependencyInjection/` sub-folder

Framework-driven; always its own sub-folder per ADR-0010 exclusion rule.
