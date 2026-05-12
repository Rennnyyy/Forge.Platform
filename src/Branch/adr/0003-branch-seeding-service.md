# 0003 — BranchSeedingService: orchestration for seeded branch and snapshot creation

- **Status**: accepted
- **Date**: 2026-05-11
- **Author**: agent

## Context

Repository ADR-0004 introduces `SeedGraphOperation` for atomically copying entity triples
into a new named graph. Branch ADR-0002 introduces `Snapshot : Branch`. An
application-level service is needed to orchestrate both: create the entity in the
management graph and populate its named graph content in a single transaction, while
enforcing the strict-failure and SemVer-uniqueness contracts.

Decisions carried forward:

- Seeding an entity IRI that does not exist in the source graph aborts the transaction
  (strict failure; no lenient fallback).
- SemVer uniqueness is enforced by an application-level SPARQL `ASK` check, not by SHACL.
- Empty-graph creation (no seeded content) does not require this service; callers use
  `EntityTransaction` directly.
- Query-driven seeding (SPARQL pattern instead of explicit IRI list) is out of scope.

## Options

**Option A** — Orchestration logic inline in HTTP endpoint handlers.
Con: duplicates the SemVer check and frozen-set invalidation across every creation path;
untestable in isolation without standing up the HTTP layer.

**Option B** — `BranchSeedingService` as a dedicated application service in `Forge.Branch`.
Pro: single place for uniqueness enforcement, transaction construction, and cache
invalidation; fully testable with store test doubles; HTTP handlers become thin delegates.

## Decision

**Option B.**

### `BranchSeedingService`

`internal sealed class BranchSeedingService` registered as a **scoped** dependency by
`AddForgeBranch()`.

Dependencies (injected):
- `[FromKeyedServices("forge.branch.management")] ITransactionalEntityStore managementStore`
- `ITransactionalEntityStore branchDataStore` — the default unkeyed store targeting the
  active branch graph (for `SeedGraphOperation`)
- `SnapshotGuardedTransactionalStore snapshotGuard` — for `InvalidateFrozenSetAsync()`
- `IOptions<BranchOptions> branchOptions`

### `CreateSeededBranchAsync`

```csharp
Task<Branch> CreateSeededBranchAsync(
    Branch branch,
    string sourceGraphIri,
    IReadOnlyList<string> entityIris,
    CancellationToken cancellationToken = default)
```

Builds and commits a single `EntityTransaction` against the management store containing:
1. `CreateOperation<Branch>` for the new branch entity.
2. `SeedGraphOperation(sourceGraphIri, branch.Iri, entityIris)` targeting the branch
   data store.

Propagates `SeedOperationMissingEntityException` unchanged to the caller.

### `CreateSnapshotAsync`

```csharp
Task<Snapshot> CreateSnapshotAsync(
    Snapshot snapshot,
    string sourceGraphIri,
    IReadOnlyList<string> entityIris,
    CancellationToken cancellationToken = default)
```

Steps:
1. **SemVer uniqueness check** (if any SemVer property on `snapshot` is non-null): issue
   a SPARQL `ASK` against the management graph. If a `Snapshot` with the same
   `(SemVerMajor, SemVerMinor, SemVerPatch, SemVerPreRelease)` tuple already exists,
   throw `SnapshotVersionConflictException` before any write.
2. Commit an `EntityTransaction` containing:
   - `CreateOperation<Snapshot>` against the management store.
   - `SeedGraphOperation(sourceGraphIri, snapshot.Iri, entityIris)` against the data store.
3. On successful commit, call `snapshotGuard.InvalidateFrozenSetAsync()` so the
   immutability guard is immediately aware of the new snapshot.

Propagates `SeedOperationMissingEntityException` and `SnapshotVersionConflictException`
unchanged.

### Two-store transaction note

The management entity write (`CreateOperation<Snapshot>`) and the data graph seed
(`SeedGraphOperation`) target different stores. They are committed as two correlated
operations: the `SeedGraphOperation` is executed first; if it throws, the management
`Create` is never issued. If the management `Create` throws after a successful seed,
the caller receives the exception and the seeded graph is an orphan — it will be cleaned
up on the next `CreateSnapshotAsync` attempt or by a manual `DropGraph`. A future ADR
may introduce a two-phase commit protocol if operational requirements demand it.

## Consequences

- SemVer uniqueness, seeding atomicity, and frozen-set invalidation are co-located in one
  service and fully testable in isolation with store test doubles.
- Empty-graph `Branch` and `Snapshot` creation bypasses this service entirely; callers
  use `EntityTransaction` directly as documented in Branch ADR-0001.
- `SnapshotVersionConflictException` is a new public exception type in `Forge.Branch`.
- HTTP endpoints `POST /snapshots` (Branch ADR-0002) delegate to `CreateSnapshotAsync`.
- Query-driven seeding is out of scope. Draft a follow-up ADR before implementing.
- The two-store correlation is a known limitation; a future ADR may address two-phase
  commit if the orphan-graph risk proves operationally significant.
