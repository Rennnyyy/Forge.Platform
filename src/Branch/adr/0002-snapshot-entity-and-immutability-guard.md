# 0002 — Snapshot entity, immutability guard, and version bounds

- **Status**: accepted
- **Date**: 2026-05-11
- **Author**: agent

## Context

Branch ADR-0001 establishes `Branch` as a mutable named graph entity. The platform
requires a mechanism to create *frozen* named graphs whose content is locked after
creation — versioned snapshots used for stable reference points or releases.

Three constraints shape the design:

1. A snapshot is mechanically identical to a branch: a named graph, addressed by IRI,
   read via `BranchScope.Use(...)`, stored in the management graph. No new storage
   mechanism is warranted.
2. Snapshots carry version metadata (`SnapshotAt`, optional SemVer properties) that
   does not belong on mutable branches.
3. Immutability must be enforced at the write-guard layer without changing the
   `IEntityStore` contract.

## Options

### Entity model

**Option A** — `Immutable : bool` flag on `Branch`.
Con: flag can be flipped by a subsequent management-graph write; nullable version
properties create awkward conditional clusters on `Branch`; the type system cannot
distinguish mutable from immutable at compile time.

**Option B** — Separate `Snapshot` entity at a distinct IRI path (`/snapshots/{Name}`).
Con: `Name`, `Description`, `CreatedAt`, and `[Identity]` are duplicated across two
classes; a second IRI path breaks the single "entity IRI = named graph IRI" pattern in
favour of a purely presentational distinction with no mechanical benefit.

**Option C** — `Snapshot : Branch` entity subtype.
Pro: inherits `Name`, `Description`, `CreatedAt`, and `[Identity]` from `Branch` with no
duplication; the generator handles entity subtypes (FORGE0007 blocks re-declaring `Path`,
so the IRI path `/branches/{Name}` is shared — coherent because the underlying mechanism
is identical); `rdf:type forge:snapshot` discriminates subtypes in the triplestore at
query time; `QueryByTypeAsync<Branch>()` returns only mutable branches,
`QueryByTypeAsync<Snapshot>()` returns only snapshots.
Con: Liskov substitution — `Snapshot` passes as `Branch` at compile time. Mitigated by
the guard layer (invariant enforced for every path reaching `ExecuteTransactionAsync`)
and application-service boundary checks.

### Immutability guard frozen-set consistency

**Option 1** — Synchronous management-graph lookup per `ExecuteTransactionAsync` call.
Con: latency on every write across all branch-scoped operations.

**Option 2** — Reactive change-notification hook on the management graph.
Con: no existing notification infrastructure; large scope expansion.

**Option 3** — Startup load + flush-on-write.
Loads `QueryByTypeAsync<Snapshot>()` into an in-memory `HashSet<string>` at startup.
Flushes and rebuilds after any transaction that creates or deletes a `Snapshot`.
A `ReaderWriterLockSlim` protects concurrent read access. Management writes are rare;
flush cost is negligible.

## Decision

**Option C + Option 3.**

### `Snapshot` entity

```csharp
[Entity(PredicatePath = "snapshot")]
public partial class Snapshot : Branch
{
    /// <summary>
    /// Logical moment the snapshot content was frozen. May differ from
    /// <see cref="Branch.CreatedAt"/> when representing a past point in time.
    /// Always set by the application service at creation time.
    /// </summary>
    [Predicate("snapshotAt")]
    public DateTimeOffset SnapshotAt { get; init; }

    [Predicate("semVerMajor")]
    public int? SemVerMajor { get; init; }

    [Predicate("semVerMinor")]
    public int? SemVerMinor { get; init; }

    [Predicate("semVerPatch")]
    public int? SemVerPatch { get; init; }

    [Predicate("semVerPreRelease")]
    public string? SemVerPreRelease { get; init; }
}
```

Design notes:
- No `[Identity]` — inherited from `Branch` (`PropertyBasedPlain`, `[IdentityPart(0)] Name`).
- No `Path` in `[Entity]` — FORGE0007 forbids it on subtypes; IRI path `/branches/{Name}`
  is inherited from `Branch`. The shared namespace is correct: mechanism is identical.
- No `SourceBranchIri` — the named graph content is the provenance. The IRI-list seeding
  model (Branch ADR-0003) allows cross-branch seeds; a stored single-source reference
  would be misleading in the general case.
- All SemVer properties are optional on the entity. The application service enforces
  that at least `SnapshotAt` is set. SemVer is entirely optional.

### SemVer uniqueness

Enforced by `BranchSeedingService.CreateSnapshotAsync()` (Branch ADR-0003): issues a
SPARQL `ASK` against the management graph before committing. Throws
`SnapshotVersionConflictException` if a snapshot with the same
`(SemVerMajor, SemVerMinor, SemVerPatch, SemVerPreRelease)` tuple already exists.
Not enforced at the SHACL layer — a per-graph uniqueness check cannot be expressed in
standard SHACL without SPARQL constraints.

### `SnapshotGuardedTransactionalStore`

A new `internal sealed` decorator wrapping the keyed management-graph
`ITransactionalEntityStore`, added to the same decorator chain as
`BranchGuardedTransactionalStore`.

Maintains a `HashSet<string>` of frozen named graph IRIs:

- Populated at startup by `SnapshotStartupService` (`IHostedService`), following the same
  pattern as `DefaultBranchStartupService`: calls `QueryByTypeAsync<Snapshot>()` on the
  management store and records every snapshot IRI in the set.
- Exposes `InvalidateFrozenSetAsync()`: flushes and rebuilds the set by re-querying the
  management store. Called by `BranchSeedingService` after a snapshot is created or
  deleted.
- On every `ExecuteTransactionAsync` call, any `CreateOperation`, `UpdateOperation`, or
  `DropGraphOperation` whose target IRI is in the frozen set is rejected with
  `SnapshotImmutabilityViolationException` — **before** the inner store is contacted.
- Exception: a `DeleteOperation` on a snapshot entity IRI paired with a
  `DropGraphOperation` on the **same** IRI within the **same** transaction is the sole
  permitted write against a frozen graph. This is the atomic cascade-delete pattern
  established by Branch ADR-0001.

### HTTP surface

- `POST /snapshots` — delegates to `BranchSeedingService.CreateSnapshotAsync`; returns `201 Created`.
- `DELETE /snapshots/{name}` — issues `EntityTransaction.Delete + DropGraph`; returns `204 No Content`.
- `GET /branches?type=snapshot` — filtered listing by `rdf:type`.
- `GET /branches?semver=1.0.0` — lookup by SemVer string (query parameter, not a path
  segment; snapshot identity is the `Name` slug, not the version string).

## Consequences

- Snapshots and branches share the `/branches/{Name}` IRI namespace. Name uniqueness
  across both types is structurally enforced by the shared entity identity.
- `QueryByTypeAsync<Branch>()` returns mutable branches only; `QueryByTypeAsync<Snapshot>()`
  returns snapshots only — discriminated by `rdf:type` at the triplestore level.
- The guard adds startup latency proportional to snapshot count (one management-graph
  query). Expected negligible in practice.
- Liskov risk is contained: the guard rejects any CUD path targeting a frozen graph
  regardless of the static type of the entity being written.
- An empty snapshot (no seeded content) is valid. Callers that do not need graph seeding
  use `EntityTransaction` directly against the management store.
- Seeding snapshot content requires `BranchSeedingService` (Branch ADR-0003), which in
  turn requires `SeedGraphOperation` (Repository ADR-0004).
- A follow-up ADR is required before implementing query-driven seeding (SPARQL pattern
  instead of explicit IRI list).
